﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Nito.AsyncEx;
using Polly;

#endregion

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
    public bool FileCached
    {
        get
        {
            _context.WaitForDrive();
            return File.Exists(FilePath);
        }
    }

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
    public Cacher(CachedResourceData cachedResourceData, UserDataContext context) : this(cachedResourceData.Uri,
        cachedResourceData.FileName, context)
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
            task.Start();
            task.Wait();
        }
    }

    private static readonly AsyncLock _mutex = new AsyncLock();

    public static AsyncLock GetCacherMutex(string password, bool ImSureINeedThis)
    {
        if (password !=
            "Yes, I know what I am doing and this method will release the lock when it is taken. And I understand that if the lock is not released cacher will no longer be able to download anything.")
        {
            throw new ArgumentException("Please make sure you know what you are doing before getting this mutex", nameof(password));
        }

        if (!ImSureINeedThis)
        {
            throw new ArgumentException("This is a very unsafe method, only do it if you know what you're doing", nameof(ImSureINeedThis));
        }
        return _mutex;
    }
    
    /// <summary>
    /// Downloads the latest file with progress reports
    /// </summary>
    public async Task DownloadUpdateAsync()
    {
        using (await _mutex.LockAsync())
        {
            using (var t = App.telemetryClient.StartOperation<RequestTelemetry>("Downloading update"))
            {
                t.Telemetry.Url = new Uri(FileURL);
                await InternetMan.WaitForInternetAsync(_context); //We need internet connectivity


                var tempDir = Path.Join(Path.GetDirectoryName(Environment.ProcessPath), "Temp");
                if (Directory.Exists("W:\\"))
                {
                    tempDir = Path.Join("W:\\", "Temp");
                }

                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                //var downloader = DownloadBuilder.New()
                //    .WithUrl(FileURL)
                //    .WithFileLocation(FilePath)
                //    .WithConfiguration(new DownloadConfiguration()
                //    {
                //        MaximumBytesPerSecond = long.MaxValue,
                //        CheckDiskSizeBeforeDownload = true,
                //        BufferBlockSize = 256 * 1024,
                //        MaxTryAgainOnFailover = int.MaxValue,
                //        OnTheFlyDownload = false,
                //        TempDirectory = tempDir,
                //    })
                //    .Build();
                

                _logger.LogInformation($"Started downloading update for {FileURL}");
                var updateWindow = await _context.DialogCoordinator.ShowProgressAsync(_context,
                    $"Downloading updated {FileName}", "Downloading updated file");
                updateWindow.SetIndeterminate();
                updateWindow.Maximum = 100;
                SetCachedFileLastModified(DateTime
                    .MinValue); //Set it to the lowest value so if we were to crash, it will re-download
                var rPolicy = Policy
                    .Handle<Exception>().WaitAndRetryForeverAsync(attempt=>5.Seconds(), onRetry: (exception, calculatedWaitDuration) => // Capture some info for logging!
                    {
                        _logger.LogError(exception, "Downloader failed with error: {outcome}", exception);
                        updateWindow.SetMessage($"Download failed, retrying in 5 seconds...");
                    });
                var response = rPolicy.ExecuteAsync(async context =>
                {
                    var downloader = new HttpClientDownloadWithProgress(FileURL, FilePath);
                    DateTime LastUpdate = DateTime.MinValue;
                    downloader.ProgressChanged += (sender, args) =>
                    {
                        var now = DateTime.UtcNow;
                        if ((now - LastUpdate).TotalMilliseconds >= 50)
                        {
                            LastUpdate = now;
                            var eta = "calculating...";
                            if (args.AverageBytesPerSecondSpeed > 0)
                            {
                                eta = ((args.TotalBytesToReceive - args.ReceivedBytesSize) /
                                       args.AverageBytesPerSecondSpeed)
                                    .Seconds().Humanize(minUnit: TimeUnit.Second, precision: 2);
                            }

                            const int space = 4;
                            var info = new List<KeyValuePair<string, string>>()
                            {
                                new("Time left:", eta),
                                new("Transferred:",
                                    $"{args.ReceivedBytesSize.Bytes().Humanize("#.00")} of {args.TotalBytesToReceive.Bytes().Humanize("#.00")}"),
                                new("Speed:",
                                    $"{args.BytesPerSecondSpeed.Bytes().Per(1.Seconds()).Humanize("#")} (avg: {args.AverageBytesPerSecondSpeed.Bytes().Per(1.Seconds()).Humanize("#")})")
                            };
                            var longest = info.MaxBy(x => x.Key.Length).Key.Length;
                            var maxLength = longest + space;
                            var sb = new StringBuilder();
                            foreach (var pair in info)
                            {
                                var newKey = pair.Key.PadRight(maxLength + 2);

                                if (pair.Key.Length == longest)
                                {
                                    newKey = pair.Key.PadRight(maxLength);
                                }

                                sb.AppendLine($"{newKey} {pair.Value}");
                            }

                            updateWindow.SetProgress(args.ProgressPercentage);
                            updateWindow.SetMessage(sb.ToString());
                        }
                    };
                    await downloader.StartDownload();
                }, CancellationToken.None);
                //downloader.DownloadFileCompleted += async (sender, args) =>
                // {
                //     if (args.Error is not null)
                //     {
                //         _logger.LogError(args.Error, "Got error {e} while downloading update", args.Error);
                //     }
                // };

                await response.WaitAsync(CancellationToken.None);
                SetCachedFileLastModified(GetLastModifiedFromWeb());
                await updateWindow.CloseAsync();
                _logger.LogInformation($"Download complete for {FileURL}");
                t.Telemetry.Success = true;
            }
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
        _context.WaitForDrive();
        if (!File.Exists(FileCacheDataPath))
        {
            return DateTime.MinValue;
        }

        try
        {
            _context.WaitForDrive();
            CacherData data = JsonConvert.DeserializeObject<CacherData>(File.ReadAllText(FileCacheDataPath));
            return data.LastModified;
        }
        catch (Exception e)
        {
            _logger.LogError(
                $"Got error {e.Message} while trying to deserialize {FileCacheDataPath}. Deleting json file.");
            _context.WaitForDrive();
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
        _context.WaitForDrive();
        return File.ReadAllText(FilePath);
    }

    public async Task<string> ReadAllTextAsync()
    {
        await _context.WaitForDriveAsync();
        return await File.ReadAllTextAsync(FilePath);
    }

    ///<summary>
    ///Deletes the data associated with the cached file
    ///</summary>
    public void Delete()
    {
        _context.WaitForDrive();
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        _context.WaitForDrive();
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
        
        _context.WaitForDrive();
        File.WriteAllText(FileCacheDataPath, JsonConvert.SerializeObject(data, Formatting.Indented));
    }
}