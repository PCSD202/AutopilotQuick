using System;
using System.Collections.Generic;
using System.Diagnostics;
using NLog;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;

namespace AutopilotQuick;

public class Cacher
{
    private static readonly ILogger<Cacher> _logger = App.GetLogger<Cacher>();

    /// <summary>
    /// The name of the file once downloaded, needs to be unique or the files will overwrite
    /// </summary>
    public readonly string FileName;

    /// <summary>
    /// The URL of where to get the file from
    /// </summary>
    public readonly string FileURL;

    /// <summary>
    /// The downloaded path of the file
    /// </summary>
    public string FilePath => Path.Combine(BaseDir, FileName);

    /// <summary>
    /// Whether or not we have the file downloaded
    /// </summary>
    /// <remarks>Does not use internet, or check that the file is up to date, for that use <see cref="IsUpToDate"/></remarks>
    public bool FileCached => File.Exists(FilePath);

    /// <summary>
    /// The cache directory
    /// </summary>
    public static string BaseDir => Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache");

    private UserDataContext _context;

    /// <summary>
    /// Is your cached file up to date? Checks the last modified header from the webserver, and the last modified of the downloaded file
    /// </summary>
    /// <remarks>Requires internet and will wait for it, if you do not want that use <see cref="FileCached"/></remarks>
    public bool IsUpToDate => GetCachedFileLastModified() >= GetLastModifiedFromWeb();
    private string FileCacheDataPath => Path.Combine(BaseDir, FileName + "-CacheData.json");

    static readonly HttpClient client = new HttpClient();

    /// <summary>
    /// Automatically caches files from a WebServer using the Last-Modified header
    /// </summary>
    /// <param name="FileURL">The URL of the file you wish to download</param>
    /// <param name="FileName">The filename that you would like to download the file to</param>
    /// <param name="context">The context of the mainwindow, this is for progress reports.</param>
    public Cacher(string FileURL, string FileName, UserDataContext context)
    {
        this.FileURL = FileURL;
        this.FileName = FileName;
        _context = context;
        if (!Directory.Exists(BaseDir))
        {
            Directory.CreateDirectory(BaseDir);
        }
    }
    
    /// <summary>
    /// Automatically caches files from a WebServer using the Last-Modified header
    /// </summary>
    /// <param name="FileURI">The URI of the file you wish to download</param>
    /// <param name="FileName">The filename that you would like to download the file to</param>
    /// <param name="context">The context of the mainwindow, this is for progress reports.</param>
    public Cacher(Uri FileURI, string FileName, UserDataContext context)
    {
        FileURL = FileURI.AbsoluteUri;
        this.FileName = FileName;
        _context = context;
        if (!Directory.Exists(BaseDir))
        {
            Directory.CreateDirectory(BaseDir);
        }
    }
    
    
    /// <summary>
    /// Automatically caches files from a WebServer using the Last-Modified header
    /// </summary>
    /// <param name="FileURI">The URI of the file you wish to download</param>
    /// <param name="FileName">The filename that you would like to download the file to</param>
    /// <param name="context">The context of the mainwindow, this is for progress reports.</param>
    public Cacher(CachedResourceData cachedResourceData, UserDataContext context) : this(cachedResourceData.Uri, cachedResourceData.FileName, context)
    {
        
    }

    private Task? DownloadUpdateTask = null;

    public void DownloadUpdate()
    {
        if (DownloadUpdateTask is not null)
        {
            DownloadUpdateTask.Wait();
        }
        else
        {
            Task task = Task.Run(async () => await DownloadUpdateAsync());
            DownloadUpdateTask = task;
            task.Wait();
        }
    }

    /// <summary>
    /// Downloads the latest file with progress reports
    /// </summary>
    public async Task DownloadUpdateAsync()
    {
        using (var t = App.telemetryClient.StartOperation<RequestTelemetry>("Downloading update"))
        {
            t.Telemetry.Url = new Uri(FileURL);
            await InternetMan.WaitForInternetAsync(_context); //We need internet connectivity
            _logger.LogInformation($"Started downloading update for {FileURL}");

            var updateWindow = await _context.DialogCoordinator.ShowProgressAsync(_context, $"Downloading updated {FileName}", "Downloading updated file");
            updateWindow.SetIndeterminate();
            updateWindow.Maximum = 100;
            SetCachedFileLastModified(DateTime.MinValue); //Set it to the lowest value so if we were to crash, it will re-download
            var rPolicy = Policy
                .Handle<Exception>().RetryForeverAsync((outcome, retryNumber, context) =>
                {
                    _logger.LogError(outcome, "Downloader failed with error: {outcome}", outcome);
                    updateWindow.SetMessage($"Download failed {retryNumber} times, retrying...");
                });
            
            // ReSharper disable once AccessToDisposedClosure
            var response = rPolicy.ExecuteAsync(async context =>
            {
                var DownloadClient = new HttpClientDownloadWithProgress(FileURL, FilePath);
                var sw = Stopwatch.StartNew();
                DownloadClient.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) =>
                {
                    if (!progressPercentage.HasValue) return;

                    var bytesPerSecond = totalBytesDownloaded.Bytes().Per(sw.Elapsed);
                    updateWindow.SetProgress(progressPercentage.Value);
                    updateWindow.SetMessage($"Downloading updated file {(progressPercentage.Value / 100):P}\n" +
                                            $"{totalBytesDownloaded.Bytes().Humanize("#.00")} of {totalFileSize.Value.Bytes().Humanize("#.00")}".PadRight(27)+ $"({bytesPerSecond.Humanize("#", TimeUnit.Second)})");
                };
                await DownloadClient.StartDownload();
            }, CancellationToken.None);
            await response.WaitAsync(CancellationToken.None);
            SetCachedFileLastModified(GetLastModifiedFromWeb());
            await updateWindow.CloseAsync();
            _logger.LogInformation($"Download complete for {FileURL}");
            t.Telemetry.Success = true;
        }
    }

    /// <summary>
    /// Gets the Last-Modified header from the internet
    /// </summary>
    /// <returns>The DateTime from the Last-Modified Header. If not found returns <see cref="DateTime.MaxValue"/></returns>
    public DateTime GetLastModifiedFromWeb()
    {
        using (var t = App.telemetryClient.StartOperation<RequestTelemetry>("Requesting last modified"))
        {
            t.Telemetry.Url = new Uri(FileURL);
            InternetMan.WaitForInternet(_context); //We need internet connectivity
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, FileURL);
                var response = client.Send(request);
                var lastModified = response.Content.Headers.LastModified;
                if (lastModified.HasValue)
                {
                    t.Telemetry.Success = true;
                    return lastModified.Value.UtcDateTime;
                }
                else
                {
                    t.Telemetry.Success = false;
                    _logger.LogError($"No Last-Modified header for {FileURL}");
                    return DateTime.MaxValue;
                }
            }
            catch (Exception e)
            {
                t.Telemetry.Success = false;
                _logger.LogError($"Got error {e} while trying to get last modified from web for {FileURL}");

                return DateTime.MaxValue;
            }
        }
    }

    /// <summary>
    /// Gets the last modified information from the cache from disk
    /// </summary>
    /// <returns>The last-modified information from disk, if not found returns <see cref="DateTime.MinValue"/></returns>
    public DateTime GetCachedFileLastModified()
    {
        if (!File.Exists(FileCacheDataPath))
        {
            return DateTime.MinValue;
        }

        try
        {
            CacherData data = JsonConvert.DeserializeObject<CacherData>(File.ReadAllText(FileCacheDataPath));
            return data.LastModified;
        }
        catch (Exception e)
        {
            _logger.LogError(
                $"Got error {e.Message} while trying to deserialize {FileCacheDataPath}. Deleting json file.");
            File.Delete(FileCacheDataPath);
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Reads the cached file as a text file
    /// </summary>
    /// <returns>The file's text</returns>
    public string ReadAllText()
    {
        return File.ReadAllText(FilePath);
    }
    
    public Task<string> ReadAllTextAsync()
    {
        return File.ReadAllTextAsync(FilePath);
    }

    ///<summary>
    ///Deletes the data associated with the cached file
    ///</summary>
    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        if (File.Exists(FileCacheDataPath))
        {
            File.Delete(FileCacheDataPath);
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
        File.WriteAllText(FileCacheDataPath, JsonConvert.SerializeObject(data, Formatting.Indented));
    }
}