using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using NLog;
using NLog.StructuredLogging.Json;
using Octokit;

namespace AutopilotQuick
{
    public class InternetMan
    {
        private static readonly InternetMan instance = new();

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        public static InternetMan getInstance()
        {
            return instance;
        }
        public bool IsConnected { get; private set; } = false;
        public event EventHandler InternetBecameAvailable;

        public static void WaitForInternet(UserDataContext context) {
            Task task = Task.Run(async () => await InternetMan.WaitForInternetAsync(context));
            task.Wait();
        }
        public static async Task WaitForInternetAsync(UserDataContext context)
        {
            
            var connectedToInternet = CheckForInternetConnection();
            if (!connectedToInternet)
            {
                var progressController = await context.DialogCoordinator.ShowProgressAsync(context, "Please wait...", "Connecting to the internet");
                progressController.SetIndeterminate();
                while (!connectedToInternet)
                {
                    connectedToInternet = CheckForInternetConnection();
                }
                await progressController.CloseAsync();
            }
            
        }
        
        public static bool CheckForInternetConnection(int timeoutMs = 10000, string url = "http://www.gstatic.com/generate_204")
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.KeepAlive = false;
                request.Timeout = timeoutMs;
                using (var response = (HttpWebResponse)request.GetResponse())
                    return true;
            }
            catch
            {
                return false;
            }
        }
        
        public static bool CheckForHTTPSConnection(int timeoutMs = 10000, string url = "https://nettools.psd202.org/AutoPilotFast/InternetTest.txt")
        {
            try
            {
                
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.KeepAlive = false;
                request.Timeout = timeoutMs;

                using (var response = (HttpWebResponse)request.GetResponse())
                    return true;
            }
            catch
            {
                return false;
            }
        }

        public void RunLoop()
        {
            while (true)
            {
                var internet = CheckForInternetConnection(1000) & CheckForHTTPSConnection(1000);
                if (internet && !IsConnected)
                {
                    _logger.Info("I decree internet is available");
                    _logger.ExtendedInfo("Internet Available");
                    IsConnected = internet;
                    InternetBecameAvailable?.Invoke(this, new EventArgs());
                    
                }
                else if(!internet && IsConnected)
                {
                    _logger.Info("Where did the internet go? Nobody knows.");
                    _logger.ExtendedInfo("Internet Lost");
                }
                IsConnected = internet;
                
                Task.Delay(IsConnected?1000*10:1000).Wait();
            }
            

        }
    }
}
