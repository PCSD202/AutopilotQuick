using Humanizer;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace AutopilotQuick.Tests;

[SingleThreaded]
public class ConnectivityServiceTests
{
    [SetUp]
    public void Setup()
    {
        
    }

    [NonParallelizable]
    [Test]
    public void InternetConnectionToInvalidURL()
    {
        var options = Options.Create<ConnectivityServiceOptions>(new ConnectivityServiceOptions());
        var logger = TestLogger.Create<ConnectivityService>();
        var cService = new ConnectivityService(logger, null, options);
        var result = cService.CheckForInternetConnection(null, "https://0.0.0.0");
        Assert.That(result, Is.False);
    }
    
    [NonParallelizable]
    [Test]
    public void InternetConnectionToValidURL()
    {
        var options = Options.Create<ConnectivityServiceOptions>(new ConnectivityServiceOptions());
        var logger = TestLogger.Create<ConnectivityService>();
        var cService = new ConnectivityService(logger, null, options);
        
        var result = cService.CheckForInternetConnection();
        Assert.That(result, Is.True);
    }
    
    [NonParallelizable]
    [Test]
    public async Task InternetConnectionInvokesConnectedEvent()
    {
        var options = Options.Create<ConnectivityServiceOptions>(new ConnectivityServiceOptions());
        var logger = TestLogger.Create<ConnectivityService>();
        var cService = new ConnectivityService(logger, null, options);
        var internetConnectedCalled = new AsyncManualResetEvent(false);
        ConnectivityService.InternetConnected += (sender, args) => internetConnectedCalled.Set();
        cService.Start();
        var cts = new CancellationTokenSource(5.Seconds());
        await internetConnectedCalled.WaitAsync(cts.Token);
        Assert.That(internetConnectedCalled.IsSet, Is.True);
    }
    
    [NonParallelizable]
    [Test]
    public async Task WaitForInternetInvokesEvent()
    {
        var options = Options.Create<ConnectivityServiceOptions>(new ConnectivityServiceOptions());
        var logger = TestLogger.Create<ConnectivityService>();
        var cService = new ConnectivityService(logger, null, options);
        bool WaitingForInternetEventCalled = false;
        bool FinishedWaitingForInternetEventCalled = false;
        ConnectivityService.StartedWaitingForInternet += (sender, args) => WaitingForInternetEventCalled = true;
        ConnectivityService.FinishedWaitingForInternet += (sender, args) => FinishedWaitingForInternetEventCalled = true;
        cService.Start();
        var cts = new CancellationTokenSource(5.Seconds());
        ConnectivityService.IsConnected = false;
        await cService.WaitForInternetAsync();
        
        Assert.Multiple(() =>
        {
            Assert.That(WaitingForInternetEventCalled, Is.True);
            Assert.That(FinishedWaitingForInternetEventCalled, Is.True);
        });
    }
}