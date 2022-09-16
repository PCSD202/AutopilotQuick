
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;
using Timer = System.Threading.Timer;

namespace AutopilotQuick
{
    internal class BatteryMan
    {

        private static readonly BatteryMan Instance = new();
        public event EventHandler<BatteryUpdatedEventData>? BatteryUpdated;

        private static readonly ILogger Logger = App.GetLogger<BatteryMan>();
        public static BatteryMan GetInstance()
        {
            return Instance;
        }
        private BatteryUpdatedEventData _status { get; set; } = new BatteryUpdatedEventData(0, false);

        public BatteryUpdatedEventData Status
        {
            get => _status;
            set
            {
                _status = value;
                BatteryUpdated?.Invoke(this, _status);
            }
        }

        public bool IsCharging
        {
            get => Status.IsCharging;
            set => Status = Status with { IsCharging = value };
        }

        public int BatteryPercent
        {
            get => Status.BatteryPercent;
            set => Status = Status with { BatteryPercent = value };
        }
        
        public bool ShouldStop { get; set; } = false;
        
        
        private Timer _timer = null;
        
        public void StartTimer()
        {
            using (App.GetTelemetryClient().StartOperation<RequestTelemetry>("Starting Battery management service"))
            {
                var tClient = App.GetTelemetryClient();
                _timer = new Timer(Run, null, 0, 100);
            }
        }
        public void Run(Object? o)
        {
            PowerStatus pwr = SystemInformation.PowerStatus;
            BatteryPercent = (int)Math.Round(pwr.BatteryLifePercent * 100, 0);
            IsCharging = pwr.PowerLineStatus == PowerLineStatus.Online;
        }
        public readonly record struct BatteryUpdatedEventData(int BatteryPercent, bool IsCharging);
    }
}
