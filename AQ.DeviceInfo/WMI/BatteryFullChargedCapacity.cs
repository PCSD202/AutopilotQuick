#region

using ORMi;

#endregion

namespace AQ.DeviceInfo.WMI;

[WMIClass("BatteryFullChargedCapacity", Namespace = "ROOT/WMI")]
public class BatteryFullChargedCapacity
{
    public ulong FullChargedCapacity { get; set; }
}