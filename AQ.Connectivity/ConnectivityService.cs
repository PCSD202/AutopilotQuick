using System.Net;
using System.Runtime.CompilerServices;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

[assembly: InternalsVisibleTo("AutopilotQuick.Tests")]
namespace AQ.Connectivity;

public class ConnectivityService
{
    private readonly ILogger<ConnectivityService> _logger;
    private readonly ConnectivityServiceOptions _options;
    private readonly TelemetryClient? _telemetryClient;
    private static readonly HttpClient _client = new HttpClient();
    
    public ConnectivityService(ILogger<ConnectivityService> logger, TelemetryClient? telemetryClient, IOptions<ConnectivityServiceOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _telemetryClient = telemetryClient;
    }

    private static Timer? _timer = null;
    private void StartTimer()
    {
        if (_timer is not null) return;
        using (_telemetryClient?.StartOperation<RequestTelemetry>("Starting connectivity service"))
        {
            _logger.LogInformation("Connectivity service started");
            _timer = new Timer(Run, null, _options.StartDelay, _options.RefreshTime); //Give some time for the app to startup before we start checking for internet
        }
    }

    public void Start() => Start(CancellationToken.None);
    public void Start(CancellationToken ct)
    {
        StartTimer();
        ct.Register(Stop);
    }
    
    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Dispose();
        }
    }
    

    public bool CheckForInternetConnection(TimeSpan? timeout = null, string url = "http://www.gstatic.com/generate_204")
    {
        timeout ??= 2.Seconds();
        try
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.KeepAlive = false;
            request.Timeout = (int)Math.Round(timeout.Value.TotalMilliseconds, 0);
            using var response = (HttpWebResponse)request.GetResponse();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Got error {e} while checking for internet connection", e);
            return false;
        }
    }
    
    public void WaitForInternet() {
        var task = Task.Run(async () => await WaitForInternetAsync());
        task.Wait();
    }
    
    public async Task WaitForInternetAsync()
    {
        using (_telemetryClient?.StartOperation<RequestTelemetry>("Waiting for internet"))
        {
            if (!IsConnected)
            {
                StartedWaitingForInternet?.Invoke(typeof(ConnectivityService), EventArgs.Empty);
                var tcs = new TaskCompletionSource();
                InternetConnected += (sender, args) => tcs.SetResult();
                await tcs.Task;
                FinishedWaitingForInternet?.Invoke(typeof(ConnectivityService), EventArgs.Empty);
            }
        }
    }
    
    
    
    
    private void Run(Object? o)
    {
        var internet = CheckForInternetConnection(_options.Timeout, "https://www.google.com");
        if (internet && !IsConnected)
        {
            _telemetryClient?.TrackEvent("InternetAvailable");
            _logger.LogInformation("I decree internet is available");
            IsConnected = internet;
            InternetConnected?.Invoke(this, EventArgs.Empty);
        }
        else if(!internet && IsConnected)
        {
            _logger.LogInformation("Where did the internet go? Nobody knows.");
            _telemetryClient?.TrackEvent("InternetLost");
            InternetLost?.Invoke(this, EventArgs.Empty);
        }
        IsConnected = internet;
    }
    public static bool IsConnected { get; internal set; } = false;
    
    public static event EventHandler InternetConnected;
    public static event EventHandler InternetLost;
    public static event EventHandler StartedWaitingForInternet;
    public static event EventHandler FinishedWaitingForInternet;
}