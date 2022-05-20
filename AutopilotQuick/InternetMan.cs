using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutopilotQuick
{
    public class InternetMan
    {
        private static readonly InternetMan instance = new();
        public static InternetMan getInstance()
        {
            return instance;
        }
        public bool IsConnected { get; private set; }
        public event EventHandler InternetBecameAvailable;


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

        public void RunLoop()
        {
            while (true)
            {
                var internet = CheckForInternetConnection(1000);
                if (internet && !IsConnected)
                {
                    InternetBecameAvailable?.Invoke(this, new EventArgs());
                }
                IsConnected = internet;
                Thread.Sleep(IsConnected?1000*10:1000);
            }
            

        }
    }
}
