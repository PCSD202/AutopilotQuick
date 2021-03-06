using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using DiskQueue;
using Newtonsoft.Json;
using NLog;

namespace AutopilotQuick.LogMan
{
    public class DurableAzureBackgroundTask
    {
        private static readonly DurableAzureBackgroundTask instance = new();
        public static DurableAzureBackgroundTask getInstance()
        {
            return instance;
        }
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        public ShareClient Share;
        public Cacher AzureLogSettingsCache;
        
        
        private void OnInternetBecameAvailable(object? sender, EventArgs e)
        {
            Share = new ShareClient(GetConnectionString(), "autopilot-quick-logs");
        }

        public string GetConnectionString()
        {
            if (!AzureLogSettingsCache.FileCached)
            {
                AzureLogSettingsCache.DownloadUpdate();
            }
            var data = JsonConvert.DeserializeObject<LogSettings>(File.ReadAllText(AzureLogSettingsCache.FilePath));
            return data?.ConnectionString ?? string.Empty;
        }

        public bool Stopped = false;
        private bool ShouldStop = false;
        public void Stop()
        {
            ShouldStop = true;
        }
        
        public void Run(UserDataContext context, CancellationToken ct)
        {
            Logger.Info("Log upload service started");
            try
            {
                AzureLogSettingsCache = new Cacher("http://nettools.psd202.org/AutoPilotFast/AzureLogSettings.json",
                    "AzureLogSettings.json", context);
                var ConnectionString = GetConnectionString();
                // Instantiate a ShareClient which will be used to create and manipulate the file share
                if (InternetMan.getInstance().IsConnected)
                {
                    Share = new ShareClient(ConnectionString, "autopilot-quick-logs");
                }
                else
                {
                    InternetMan.getInstance().InternetBecameAvailable += OnInternetBecameAvailable;
                }

                while (!ct.IsCancellationRequested && !ShouldStop)
                {
                    if (InternetMan.getInstance().IsConnected)
                    {
                        try
                        {
                            SyncLogs();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }

                    }

                    Thread.Sleep(1000);
                }

            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                Stopped = true;
            }

            Stopped = true;

        }

        public void SyncLogs()
        {
            var appFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var logFolder = $"{appFolder}/logs/";

            Task task = Task.Run(async () => await CreateShareAsync("autopilot-quick-logs"));
            task.Wait();
            var client = Share.GetDirectoryClient(DeviceID.DeviceIdentifierMan.getInstance().GetDeviceIdentifier());
            client.CreateIfNotExists();
            foreach (var update in ComputeFilesToUpload())
            {
                try
                {
                    var filePath = Path.Join(logFolder, update);
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                    var fInfo = new FileInfo(filePath);
                    var fClient = client.GetFileClient(update);
                    fClient.Create(fInfo.Length);
                    var response = client.GetFileClient(update).Upload(fs);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"File {Path.GetFileName(update)} open and cannot be opened again...");
                }
            }

            foreach (var update in ComputeFilesToDelete())
            {
                var fClient = client.GetFileClient(update);
                var response = fClient.DeleteIfExists();
            }
        }
        public List<string> ComputeFilesToUpload()
        {
            var lastModifiedLocal = GetLastModifiedForLocal();
            var lastModifiedRemote = GetLastModifiedForRemote();
            var updates = new List<string>();
            foreach (var localFile in lastModifiedLocal)
            {
                if (lastModifiedRemote.ContainsKey(localFile.Key))
                {
                    if (localFile.Value >= lastModifiedRemote[localFile.Key])
                    {
                        updates.Add(localFile.Key);
                    }
                }
                else
                {
                    updates.Add(localFile.Key);
                }
            }

            return updates;
        }

        public List<string> ComputeFilesToDelete()
        {
            var lastModifiedLocal = GetLastModifiedForLocal();
            var lastModifiedRemote = GetLastModifiedForRemote();
            var updates = (from remoteFile in lastModifiedRemote where !lastModifiedLocal.ContainsKey(remoteFile.Key) select remoteFile.Key).ToList();
            return updates;
        }

        public Dictionary<string, DateTime> GetLastModifiedForLocal()
        {
            var output = new Dictionary<string, DateTime>();
            var appFolder = Path.GetDirectoryName(Environment.ProcessPath);
            var logFolder = $"{appFolder}/logs/";
            var files = Directory.GetFiles(logFolder);
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var lastModified = File.GetLastWriteTimeUtc(filePath);
                output.Add(fileName, lastModified);
            }
            return output;
        }

        public Dictionary<string, DateTime> GetLastModifiedForRemote()
        {
            try
            {
                var output = new Dictionary<string, DateTime>();
                var files = GetFilesOnAzure();
                foreach (var fileItem in files)
                {
                    var lastModified = DateTime.MinValue;
                    if (fileItem.Properties.LastWrittenOn.HasValue)
                    {
                        lastModified = fileItem.Properties.LastWrittenOn.Value.DateTime.ToUniversalTime();
                    }

                    var filename = fileItem.Name;
                    output.Add(filename, lastModified);
                }

                return output;
            }
            catch
            {
                return new Dictionary<string, DateTime>();
            }
            
        }

        public List<ShareFileItem> GetFilesOnAzure()
        {
            var files = new List<ShareFileItem>();
            var remaining = new Queue<ShareDirectoryClient>();
            Share.GetDirectoryClient("logs").CreateIfNotExists();
            remaining.Enqueue(Share.GetDirectoryClient("logs"));
            while (remaining.Count > 0)
            {
                // Get all of the next directory's files and subdirectories
                ShareDirectoryClient dir = remaining.Dequeue();
                foreach (ShareFileItem item in dir.GetFilesAndDirectories())
                {

                    // Keep walking down directories
                    if (item.IsDirectory)
                    {
                        remaining.Enqueue(dir.GetSubdirectoryClient(item.Name));
                    }
                    else
                    {
                        files.Add(item);
                    }
                }
            }

            return files;
        }

        public async Task CreateShareAsync(string shareName)
        {
            // Get the connection string from app settings
            string connectionString = GetConnectionString();

            // Instantiate a ShareClient which will be used to create and manipulate the file share
            ShareClient share = new ShareClient(connectionString, shareName);

            // Create the share if it doesn't already exist
            await share.CreateIfNotExistsAsync();

            // Ensure that the share exists
            if (!await share.ExistsAsync())
            {
                Console.WriteLine($"CreateShareAsync failed");
            }
        }

    }
}
