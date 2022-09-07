using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using AQ.WebFileCacher;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AutopilotQuick.Tests;

[SingleThreaded]
public class WebFileCacherTests
{
    static readonly HttpClient client = new HttpClient();
    private static readonly string FileURL = "https://nettools.psd202.org/AutoPilotFast/InternetTest.txt";
    private ConnectivityService _connectivityService;
    
    [SetUp]
    public void Setup()
    {
        var options = new ConnectivityServiceOptions()
        {
            StartDelay = 0.Seconds()
        };
        var connectivityService = new ConnectivityService(TestLogger.Create<ConnectivityService>(), null,
            new OptionsWrapper<ConnectivityServiceOptions>(options));
        _connectivityService = connectivityService;
    }

    [Test]
    public void GetLastModifiedFromWeb()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, FileURL);
        var response = client.Send(request);
        var lasModifiedResponse = response.Content.Headers.LastModified;
        Assert.That(lasModifiedResponse, Is.Not.Null);
        Assert.That(lasModifiedResponse.HasValue, Is.True);

        Debug.Assert(lasModifiedResponse != null, nameof(lasModifiedResponse) + " != null");
        var ExpectedlastModified = lasModifiedResponse.Value.UtcDateTime;

        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { @"C:\Cache", new MockDirectoryData() }
            });
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL,
            "InternetTest.txt");
        var lastModified = internetTestCacher.GetLastModifiedFromWeb();
        
        Assert.That(lastModified, Is.EqualTo(ExpectedlastModified));
    }
    
    [Test]
    public void GetLastModifiedFromFileNonExistent()
    {
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = new MockFileSystem(new Dictionary<string, MockFileData>()
            {
                { @"C:\Cache", new MockDirectoryData() },
            });
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL,
            "InternetTest.txt");
        var lastModified = internetTestCacher.GetCachedFileLastModified();
        
        Assert.That(lastModified, Is.EqualTo(DateTime.MinValue));
    }
    
    [Test]
    public void GetLastModifiedFromCorruptDeletesCache()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() },
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL,
            "InternetTest.txt");
        mockfs.File.WriteAllText(internetTestCacher.FileCacheDataPath, "CORRUPT");
        
        var lastModified = internetTestCacher.GetCachedFileLastModified();
        Assert.Multiple(() =>
        {
            Assert.That(lastModified, Is.EqualTo(DateTime.MinValue));
            Assert.That(mockfs.File.Exists(internetTestCacher.FileCacheDataPath), Is.False);
        });
    }
    
    [Test]
    public async Task DownloadUpdateFirstTimeTest()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() },
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL,
            "InternetTest.txt");

        await internetTestCacher.DownloadUpdateAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(mockfs.File.Exists(internetTestCacher.FilePath), Is.True, "FilePath should exist after download update");
            Assert.That(mockfs.File.Exists(internetTestCacher.FileCacheDataPath), Is.True, "FileCacheDataPath should exist after downloading update");
        });
    }
    
    [Test]
    public async Task DownloadUpdateInvokesEvents()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() },
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");

        int startedFired = 0;
        int changedFired = 0;
        int completedFired = 0;
        WebFileCacher.DownloadStarted += (o, e) => startedFired++;
        WebFileCacher.DownloadProgressUpdated += (o, e) => changedFired++;
        WebFileCacher.DownloadCompleted += (o, e) => completedFired++;

        await internetTestCacher.DownloadUpdateAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(startedFired, Is.EqualTo(1), "WebfileCacher.DownloadStarted should be called once download start");
            Assert.That(changedFired, Is.GreaterThanOrEqualTo(1), "WebfileCacher.DownloadProgressUpdated should be called at least once during download");
            Assert.That(completedFired, Is.EqualTo(1), "WebfileCacher.DownloadCompleted should be called once when download is finished");
        });
    }
    
    [Test]
    public void DeleteDeletesData()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() },
            {@"C:\Cache\InternetTest.txt", new MockFileData("TEST")},
            {@"C:\Cache\InternetTest.txt-CacheData.json", new MockFileData("TEST")},
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");

        internetTestCacher.Delete();
        Assert.That(mockfs.File.Exists(internetTestCacher.FilePath), Is.False, "Need to delete FilePath when delete() is called");
        Assert.That(mockfs.File.Exists(internetTestCacher.FileCacheDataPath), Is.False, "Need to delete FileCacheDataPath when delete() is called");
    }
    
    [Test]
    public void IsUpToDateReturnsFalseWhenNoData()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() }
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");

        var upToDate = internetTestCacher.IsUpToDate;
        Assert.That(upToDate, Is.False, "Up to date should be false when file does not exist");
    }
    
    [Test]
    public void SetLastModifiedSets()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() }
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var date = DateTime.UtcNow;
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");
        internetTestCacher.SetCachedFileLastModified(date);

        var gotLastModified = internetTestCacher.GetCachedFileLastModified();
        
        Assert.That(gotLastModified, Is.EqualTo(date), "Date set with Set() and gotten with Get() should be equal");
    }
    
    [Test]
    public void CreatesCacheDirectory()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>());
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");

        Assert.That(mockfs.Directory.Exists(WebFileCacherConfig.BaseDir), Is.True, "Should create cache directory on instantiation");
    }
    
    [Test]
    public async Task DownloadReplacesFile()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            {@"C:\Cache\InternetTest.txt", new MockFileData("TEST")}
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");
        await internetTestCacher.DownloadUpdateAsync(CancellationToken.None);

        var newFileText = await mockfs.File.ReadAllTextAsync(internetTestCacher.FilePath);
        Assert.That(newFileText, Is.Not.EqualTo("TEST"), "Should replace file when downloading new one");
    }
    
    [Test]
    public void DownloadSync()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() }
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        
        var date = DateTime.UtcNow;
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");
        internetTestCacher.DownloadUpdate(CancellationToken.None);
        Assert.That(mockfs.File.Exists(internetTestCacher.FilePath), Is.True, "File should exist after downloading");
    }
    
    
    [Test]
    public void DownloadUpdateSyncInvokesEvents()
    {
        var mockfs = new MockFileSystem(new Dictionary<string, MockFileData>()
        {
            { @"C:\Cache", new MockDirectoryData() },
        });
        WebFileCacherConfig.Configure(x =>
        {
            x.BaseDir = @"C:\Cache";
            x.FileSystem = mockfs; 
            x.TelemetryClient = null;
            x.WebFileLogger = TestLogger.Create<WebFileCacher>();
            x.ConnectivityService = _connectivityService;
        });
        var internetTestCacher = new WebFileCacher(FileURL, "InternetTest.txt");

        int startedFired = 0;
        int changedFired = 0;
        int completedFired = 0;
        WebFileCacher.DownloadStarted += (o, e) => startedFired++;
        WebFileCacher.DownloadProgressUpdated += (o, e) => changedFired++;
        WebFileCacher.DownloadCompleted += (o, e) => completedFired++;

        internetTestCacher.DownloadUpdate(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(startedFired, Is.EqualTo(1), "WebfileCacher.DownloadStarted should be called once download start");
            Assert.That(changedFired, Is.GreaterThanOrEqualTo(1), "WebfileCacher.DownloadProgressUpdated should be called at least once during download");
            Assert.That(completedFired, Is.EqualTo(1), "WebfileCacher.DownloadCompleted should be called once when download is finished");
        });
    }
}