#region

using ORMi;

#endregion

namespace AQ.DeviceInfo.WMI;
[WMIClass(Name = "BatteryStaticData", Namespace = "ROOT/WMI")]
public class BatteryStaticData
{
    public ulong DesignedCapacity { get; set; }
}