using System;
using NLog;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Humanizer;
using Newtonsoft.Json;

namespace AutopilotQuick; 

public class Cacher {
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    //The name of the file once downloaded. Needs to be unique or files will overwrite.
    public readonly string FileName;
    
    //The URL of where to get the file from
    public readonly string FileURL;
    
    //The downloaded path of the file
    public string FilePath => Path.Combine(BaseDir, FileName);

    //Whether or not we have the file downloaded
    public bool FileCached => File.Exists(FilePath);
    
    public static string BaseDir => Path.Combine(Path.GetDirectoryName(App.GetExecutablePath()), "Cache");

    private UserDataContext _context;

    public bool IsUpToDate => GetCachedFileLastModified() >= GetLastModifiedFromWeb();
    private string FileCacheDataPath => Path.Combine(BaseDir, FileName + "-CacheData.json");
    
    static readonly HttpClient client = new HttpClient();

    public Cacher(string FileURL, string FileName, UserDataContext context) {
        this.FileURL = FileURL;
        this.FileName = FileName;
        _context = context;
        if (!Directory.Exists(BaseDir)) {
            Directory.CreateDirectory(BaseDir);
        }
    }

    private Task? DownloadUpdateTask = null;

    public void DownloadUpdate() {
        if(DownloadUpdateTask is not null)
        {
            DownloadUpdateTask.Wait();
        } else {
            Task task = Task.Run(async () => await DownloadUpdateAsync());
            DownloadUpdateTask = task;
            task.Wait();
        }
        
    }

    public async Task DownloadUpdateAsync() {
        await InternetMan.WaitForInternetAsync(_context); //We need internet connectivity
        _logger.Info($"Started downloading update for {FileURL}");
        
        var updateWindow = await _context.DialogCoordinator.ShowProgressAsync(_context,
            $"Downloading updated {FileName}",
            "Downloading updated file");
        updateWindow.SetIndeterminate();
        updateWindow.Maximum = 100;
        SetCachedFileLastModified(DateTime.MinValue); //Set it to the lowest value so if we were to crash, it will re-download
        using var DownloadClient = new HttpClientDownloadWithProgress(FileURL, FilePath);
        DownloadClient.ProgressChanged += (totalFileSize, totalBytesDownloaded, progressPercentage) => {
            if (!progressPercentage.HasValue) return;
            updateWindow.SetProgress(progressPercentage.Value);
            updateWindow.SetMessage($"Downloading updated file\n"+
                                    $"Progress: {(progressPercentage.Value/100):P} " +
                                    $"({totalBytesDownloaded.Bytes().Humanize("#.##")}/{totalFileSize.Value.Bytes().Humanize("#.##")})");
        };
        await DownloadClient.StartDownload();
        SetCachedFileLastModified(GetLastModifiedFromWeb());
        await updateWindow.CloseAsync();
        _logger.Info($"Download complete for {FileURL}");
    }
    
    public DateTime GetLastModifiedFromWeb() {
        InternetMan.WaitForInternet(_context); //We need internet connectivity
        try {
            var request = new HttpRequestMessage(HttpMethod.Head, FileURL);
            var response = client.Send(request);
            var lastModified = response.Content.Headers.LastModified;
            if (lastModified.HasValue) {
                return lastModified.Value.UtcDateTime;
            }
            else {
                _logger.Error($"No Last-Modified header for {FileURL}");
                return DateTime.MaxValue;
            }
        }
        catch (Exception e) {
            _logger.Error($"Got error {e} while trying to get last modified from web for {FileURL}");
            return DateTime.MaxValue;
        }
    }

    public DateTime GetCachedFileLastModified() {
        if (!File.Exists(FileCacheDataPath)) {
            return DateTime.MinValue;
        }

        try {
            CacherData data = JsonConvert.DeserializeObject<CacherData>(File.ReadAllText(FileCacheDataPath));
            return data.LastModified;
        } catch (Exception e) {
            _logger.Error($"Got error {e.Message} while trying to deserialize {FileCacheDataPath}. Deleting json file.");
            File.Delete(FileCacheDataPath);
            return DateTime.MinValue;
        }
        
    }

    public void SetCachedFileLastModified(DateTime LastModified) {
        CacherData data = new CacherData() {
            LastModified = LastModified.ToUniversalTime()
        };
        File.WriteAllText(FileCacheDataPath, JsonConvert.SerializeObject(data, Formatting.Indented));
    }
}