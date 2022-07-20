using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutopilotQuick
{
    internal class BatteryMan
    {

        private static readonly BatteryMan instance = new();
        public event EventHandler<BatteryUpdatedEventData> BatteryUpdated;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public static BatteryMan getInstance()
        {
            return instance;
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
        
        public void RunLoop()
        {
            while (true)
            {
                PowerStatus pwr = SystemInformation.PowerStatus;

                BatteryPercent = (int)Math.Round(pwr.BatteryLifePercent * 100, 0);

                IsCharging = pwr.PowerLineStatus == PowerLineStatus.Online;
                Thread.Sleep(500);
            }
            
        }
        public readonly record struct BatteryUpdatedEventData(int BatteryPercent, bool IsCharging);
    }
}
