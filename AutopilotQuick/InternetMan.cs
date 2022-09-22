using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Octokit;

namespace AutopilotQuick
{
    public class InternetMan
    {
        private static readonly InternetMan Instance = new();

        private static readonly ILogger Logger = App.GetLogger<InternetMan>();
        public static InternetMan GetInstance()
        {
            return Instance;
        }
        public bool IsConnected { get; private set; } = false;
        public event EventHandler? InternetBecameAvailable;
        public event EventHandler? InternetBecameUnavailable;
        
        private Timer _timer = null;
        
        public void StartTimer()
        {
            using (App.GetTelemetryClient().StartOperation<RequestTelemetry>("Starting InternetMan service"))
            {
                var tClient = App.GetTelemetryClient();
                tClient.TrackEvent("InternetManServiceServiceStarted");
                Logger.LogInformation("Internet man service started");
                _timer = new Timer(Run, null, 5.Seconds(), 5.Seconds()); //Give some time for the app to startup before we start checking for internet
            }
            
        }
        
        public static void WaitForInternet(UserDataContext context) {
            Task task = Task.Run(async () => await InternetMan.WaitForInternetAsync(context));
            task.Wait();
        }
        public static async Task WaitForInternetAsync(UserDataContext context)
        {
            using (App.GetTelemetryClient().StartOperation<RequestTelemetry>("Waiting for internet"))
            {
                if (!InternetMan.GetInstance().IsConnected || !NetworkInterface.GetIsNetworkAvailable())
                {
                    var progressController =
                        await context.DialogCoordinator.ShowProgressAsync(context, "Please wait...",
                            "Connecting to the internet");
                    progressController.SetIndeterminate();
                    var tcs = new TaskCompletionSource();
                    GetInstance().InternetBecameAvailable += (sender, args) =>
                    {
                        Logger.LogInformation("Internet available, setting the result of task");
                        tcs.SetResult();
                    };
                    await tcs.Task;
                    await progressController.CloseAsync();
                }
            }
        }
        
        public static bool CheckForInternetConnection(int timeoutMs = 10000, string url = "http://www.gstatic.com/generate_204")
        {
            try
            {
#pragma warning disable SYSLIB0014
                var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
                request.KeepAlive = false;
                request.Timeout = timeoutMs;
                using var response = (HttpWebResponse)request.GetResponse();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Run(Object? o)
        {
            if(!NetworkInterface.GetIsNetworkAvailable()){ return; } //No network available so don't even try
            var internet = CheckForInternetConnection(500, "https://www.google.com");
            if (internet && !IsConnected)
            {
                App.GetTelemetryClient().TrackEvent("InternetAvailable");
                Logger.LogInformation("I decree internet is available");
                IsConnected = internet;
                InternetBecameAvailable?.Invoke(this, EventArgs.Empty);
            
            }
            else if(!internet && IsConnected)
            {
                Logger.LogInformation("Where did the internet go? Nobody knows.");
                App.GetTelemetryClient().TrackEvent("InternetLost");
                InternetBecameUnavailable?.Invoke(this, EventArgs.Empty);
            }
            IsConnected = internet;
        }
    }
}
