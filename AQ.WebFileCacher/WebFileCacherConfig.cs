using System.IO.Abstractions;
using AQ.Connectivity;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace AQ.WebFileCacher;


public class WebFileCacherConfigurable
{
    public string BaseDir;

    public ILogger<WebFileCacher>? WebFileLogger;

    public TelemetryClient? TelemetryClient;

    public IFileSystem FileSystem = new FileSystem();

    public ConnectivityService ConnectivityService;
}
public static class WebFileCacherConfig
{
    internal static string BaseDir;

    internal static ILogger<WebFileCacher>? WebFileLogger;

    internal static TelemetryClient? TelemetryClient;

    internal static IFileSystem FileSystem = new FileSystem();
    internal static ConnectivityService _connectivityService;

    public static void Configure(Action<WebFileCacherConfigurable> options)
    {
        var newOption = new WebFileCacherConfigurable();
        options(newOption);
        BaseDir = newOption.BaseDir;
        WebFileLogger = newOption.WebFileLogger;
        TelemetryClient = newOption.TelemetryClient;
        FileSystem = newOption.FileSystem;
        _connectivityService = newOption.ConnectivityService;
    }
}