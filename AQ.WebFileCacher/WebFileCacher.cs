using System.Diagnostics;
using System.Runtime.CompilerServices;
using AQ.Connectivity;
using Humanizer;
using Humanizer.Bytes;
using Humanizer.Localisation;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

[assembly: InternalsVisibleTo("AutopilotQuick.Tests")]
namespace AQ.WebFileCacher;


public record struct CacherData(DateTime LastModified);
public class WebFileCacher
{
    //Triggered once on download started
    public static event EventHandler<DownloadStartedEventArgs> DownloadStarted;
    
    //Triggered when progress updates occur
    public static event EventHandler<DownloadProgressChangedEventArgs> DownloadProgressUpdated;

    //Triggered once when download completed
    public static event EventHandler<DownloadStartedEventArgs> DownloadCompleted;
    
    //The name of the file once downloaded. Needs to be unique or files will overwrite.
    public readonly string FileName;

    //The URL of where to get the file from
    public readonly string FileURL;
    
    //The downloaded path of the file
    public string FilePath => Path.Combine(WebFileCacherConfig.BaseDir, FileName);

    //Whether or not we have the file downloaded
    public bool FileCached => WebFileCacherConfig.FileSystem.File.Exists(FilePath);
    
    public bool IsUpToDate => GetCachedFileLastModified() >= GetLastModifiedFromWeb();
    internal string FileCacheDataPath => Path.Combine(WebFileCacherConfig.BaseDir, FileName + "-CacheData.json");
    
    static readonly HttpClient client = new HttpClient();

    public WebFileCacher(string FileURL, string FileName)
    {
        this.FileURL = FileURL;
        this.FileName = FileName;
        if (!WebFileCacherConfig.FileSystem.Directory.Exists(WebFileCacherConfig.BaseDir))
        {
            WebFileCacherConfig.FileSystem.Directory.CreateDirectory(WebFileCacherConfig.BaseDir);
        }
    }
    


    /// <summary>
    /// Downloads the latest file with progress reports
    /// </summary>
    public async Task DownloadUpdateAsync(CancellationToken? ct)
    {
        ct ??= CancellationToken.None;
        using var t = WebFileCacherConfig.TelemetryClient?.StartOperation<RequestTelemetry>("Downloading update");
        if (t is not null)
        {
            t.Telemetry.Url = new Uri(FileURL);
        }

        await WebFileCacherConfig._connectivityService.WaitForInternetAsync();//We need internet connectivity
        
        WebFileCacherConfig.WebFileLogger?.LogInformation($"Started downloading update for {FileURL}");

        DownloadStarted?.Invoke(typeof(WebFileCacher), new DownloadStartedEventArgs(){FileName = FileName, FileURL = FileURL});
        SetCachedFileLastModified(DateTime
            .MinValue); //Set it to the lowest value so if we were to crash, it will re-download
        using var DownloadClient = new HttpClientDownloadWithProgress(FileURL, FilePath, WebFileCacherConfig.FileSystem);

        var sw = Stopwatch.StartNew();
        DownloadClient.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
        {
            if (!progressPercentage.HasValue) return;

            var bytesPerSecond = totalBytesDownloaded.Bytes().Per(sw.Elapsed);
            DownloadProgressUpdated?.Invoke(typeof(WebFileCacher), new DownloadProgressChangedEventArgs()
            {
                byteRate = bytesPerSecond,
                Progress = progressPercentage.Value,
                totalBytesDownloaded = totalBytesDownloaded,
                totalFileSize = totalFileSize
            });
        };
        await DownloadClient.StartDownload(ct.Value);
        sw.Stop();
        SetCachedFileLastModified(GetLastModifiedFromWeb());
        DownloadCompleted?.Invoke(typeof(WebFileCacher), new DownloadStartedEventArgs {FileName = FileName, FileURL = FileURL});
        WebFileCacherConfig.WebFileLogger?.LogInformation($"Download complete for {FileURL}");
        if (t is not null)
        {
            t.Telemetry.Success = true;
        }
    }
    
    /// <summary>
    /// Gets the Last-Modified header from the internet
    /// </summary>
    /// <returns>The DateTime from the Last-Modified Header. If not found returns <see cref="DateTime.MaxValue"/></returns>
    public DateTime GetLastModifiedFromWeb()
    {
        using var t = WebFileCacherConfig.TelemetryClient?.StartOperation<RequestTelemetry>("Requesting last modified");
        if (t is not null)
        {
            t.Telemetry.Url = new Uri(FileURL); 
        }
        WebFileCacherConfig._connectivityService.WaitForInternet();//We need internet connectivity
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, FileURL);
            var response = client.Send(request);
            var lastModified = response.Content.Headers.LastModified;
            if (lastModified.HasValue)
            {
                if (t is not null)
                {
                    t.Telemetry.Success = true;
                }

                return lastModified.Value.UtcDateTime;
            }
            else
            {
                if (t is not null)
                {
                    t.Telemetry.Success = false;
                }

                WebFileCacherConfig.WebFileLogger?.LogError($"No Last-Modified header for {FileURL}");
                return DateTime.MaxValue;
            }
        }
        catch (Exception e)
        {
            if (t is not null)
            {
                t.Telemetry.Success = false;
            }
            
            WebFileCacherConfig.WebFileLogger?.LogError(e, $"Got error {e} while trying to get last modified from web for {FileURL}");

            return DateTime.MaxValue;
        }
    }
    
    private Task? DownloadUpdateTask = null;
    public void DownloadUpdate(CancellationToken? ct)
    {
        ct ??= CancellationToken.None;
        if (DownloadUpdateTask is not null)
        {
            DownloadUpdateTask.Wait();
        }
        else
        {
            var task = Task.Run(async () => await DownloadUpdateAsync(ct.Value));
            DownloadUpdateTask = task;
            task.Wait();
        }
    }
    
    
    /// <summary>
    /// Gets the last modified information from the cache from disk
    /// </summary>
    /// <returns>The last-modified information from disk, if not found returns <see cref="DateTime.MinValue"/></returns>
    public DateTime GetCachedFileLastModified()
    {
        if (!WebFileCacherConfig.FileSystem.File.Exists(FileCacheDataPath))
        {
            return DateTime.MinValue;
        }

        try
        {
            CacherData data = JsonConvert.DeserializeObject<CacherData>(WebFileCacherConfig.FileSystem.File.ReadAllText(FileCacheDataPath));
            return data.LastModified;
        }
        catch (Exception e)
        {
            WebFileCacherConfig.WebFileLogger?.LogError(e, $"Got error {e.Message} while trying to deserialize {FileCacheDataPath}. Deleting json file.");
            WebFileCacherConfig.FileSystem.File.Delete(FileCacheDataPath);
            return DateTime.MinValue;
        }
    }
    
    ///<summary>
    ///Deletes the data associated with the cached file
    ///</summary>
    public void Delete()
    {
        if (WebFileCacherConfig.FileSystem.File.Exists(FilePath))
        {
            WebFileCacherConfig.FileSystem.File.Delete(FilePath);
        }

        if (WebFileCacherConfig.FileSystem.File.Exists(FileCacheDataPath))
        {
            WebFileCacherConfig.FileSystem.File.Delete(FileCacheDataPath);
        }
    }
    
    ///<summary>
    ///Sets the last modified information for the cache
    ///</summary>
    ///<param name="LastModified">The time that you would like to be set in the cache on disk</param>
    public void SetCachedFileLastModified(DateTime LastModified)
    {
        CacherData data = new CacherData()
        {
            LastModified = LastModified.ToUniversalTime()
        };
        WebFileCacherConfig.FileSystem.File.WriteAllText(FileCacheDataPath, JsonConvert.SerializeObject(data, Formatting.Indented));
    }


    public class DownloadStartedEventArgs : EventArgs
    {
        public string FileName { get; set; }
        public string FileURL { get; set; }
    }
    
    public class DownloadProgressChangedEventArgs : EventArgs
    {
        public double Progress { get; set; }
        public long? totalFileSize { get; set; }
        public long totalBytesDownloaded { get; set; }
        public ByteRate byteRate { get; set; }
    }
    
}