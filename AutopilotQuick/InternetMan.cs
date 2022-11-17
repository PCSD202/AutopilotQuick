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
using Nito.AsyncEx;
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

        private static AsyncManualResetEvent InternetAvailable = new AsyncManualResetEvent();
        public event EventHandler? InternetBecameUnavailable;
        
        private Timer _timer = null;
        private UserDataContext _context;
        
        public void StartTimer(UserDataContext context)
        {
            using (App.GetTelemetryClient().StartOperation<RequestTelemetry>("Starting InternetMan service"))
            {
                var tClient = App.GetTelemetryClient();
                tClient.TrackEvent("InternetManServiceServiceStarted");
                Logger.LogInformation("Internet man service started");
                _context = context;
                
                _timer = new Timer(Run, null,
#if DEBUG
                    0.Seconds(),           
#endif
#if !DEBUG
                    5.Seconds(),           
#endif
                    
                    5.Seconds()); //Give some time for the app to startup before we start checking for internet
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
                    await InternetAvailable.WaitAsync();
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

        private void UpdateStatus()
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            if (adapters.All(x => x.NetworkInterfaceType is not (NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)))
            {
                _context.ConnectedToInternet = InternetConnectionStatus.NoAdapter;
            }
            else
            {
                _context.ConnectedToInternet = IsConnected?InternetConnectionStatus.Connected:InternetConnectionStatus.Disconnected;
            }
        }
        public void Run(Object? o)
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                UpdateStatus();
                return;
            } //No network available so don't even try
            var internet = CheckForInternetConnection(1000, "https://www.google.com");
            if (internet && !IsConnected)
            {
                App.GetTelemetryClient().TrackEvent("InternetAvailable");
                Logger.LogInformation("I decree internet is available");
                IsConnected = internet;
                UpdateStatus();
                InternetAvailable.Set();
                InternetBecameAvailable?.Invoke(this, EventArgs.Empty);
                Logger.LogInformation("InternetAvailabe event finished firing");
            
            }
            else if(!internet && IsConnected)
            {
                Logger.LogInformation("Where did the internet go? Nobody knows.");
                App.GetTelemetryClient().TrackEvent("InternetLost");
                InternetAvailable.Reset();
                UpdateStatus();
                InternetBecameUnavailable?.Invoke(this, EventArgs.Empty);
            }
            IsConnected = internet;
            
        }
    }
}
