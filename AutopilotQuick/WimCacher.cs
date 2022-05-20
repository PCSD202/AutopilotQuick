using Humanizer;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutopilotQuick
{
    public class WimCacher
    {
        //The path of the downloaded wim
        public string WimPath;
        
        //The URL of the wim file
        private string WimURL;

        private UserDataContext context;

        public static string CacheDir = Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache");
        public WimCacher(string FileURL, UserDataContext dataContext)
        {
            WimURL = FileURL;
            WimPath = Path.Combine(CacheDir, "image.wim");
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }
            context = dataContext;
        }

        public async Task DownloadUpdatedISO()
        {
            var updateWindow = await context.DialogCoordinator.ShowProgressAsync(context, "Downloading update", "Downloading updated windows installation file");
            updateWindow.SetIndeterminate();
            while (!MainWindow.CheckForInternetConnection())
            {
                await Task.Delay(200);
            }
            updateWindow.Maximum = 100;
            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += (sender, args) =>
                {
                    updateWindow.SetProgress(((double)args.BytesReceived/(double)args.TotalBytesToReceive)*100);
                    updateWindow.SetMessage($"Downloading updated windows installation file\n" +
                        $"Progress: {((double)args.BytesReceived / (double)args.TotalBytesToReceive):P2} ({args.BytesReceived.Bytes().Humanize("#.##")}/{args.TotalBytesToReceive.Bytes().Humanize("#.##")})\n");
                };
                client.DownloadFileCompleted += async (sender, args) =>
                {
                    SetCacheLastModified(GetLastModifiedFromWeb());
                    await updateWindow.CloseAsync();
                };
                _ = client.DownloadFileTaskAsync(WimURL, WimPath);
            }
           
        }

        public bool IsUpToDate()
        {
            return GetCacheLastModified() > GetLastModifiedFromWeb();
        }

        public DateTime GetLastModifiedFromWeb()
        {
            while (!MainWindow.CheckForInternetConnection())
            {
                Thread.Sleep(200);
            }
            System.Net.WebRequest request = System.Net.WebRequest.Create(WimURL);
            request.Method = "HEAD";
            var response = request.GetResponse();
            var lastModified = response.Headers["Last-Modified"];
            if (lastModified != null)
            {
                DateTime.TryParse(lastModified, out DateTime date);
                if (date != null)
                {
                    return date;
                }
            }
            return DateTime.MinValue;
        }

        public DateTime GetCacheLastModified()
        {
            if(!File.Exists(Path.Combine(CacheDir, "image.json")))
            {
                return DateTime.MinValue;
            }
            WimCacheData data = JsonConvert.DeserializeObject<WimCacheData>(File.ReadAllText(Path.Combine(CacheDir, "image.json")));
            return data.LastModified;
        }

        public void SetCacheLastModified(DateTime LastModified)
        {
            WimCacheData data = new WimCacheData
            {
                LastModified = LastModified.ToUniversalTime()
            };
            File.WriteAllText(Path.Combine(CacheDir, "image.json"), JsonConvert.SerializeObject(data));
        }
    }
}
