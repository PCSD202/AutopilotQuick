#region

using ORMi;

#endregion

namespace AQ.DeviceInfo.WMI;

[WMIClass("Win32_Battery")]
public class Battery
{
    public ushort Availability { get; set; }
    public uint BatteryRechargeTime { get; set; }
    public ushort BatteryStatus { get; set; }
    public string Caption { get; set; }
    public ushort Chemistry { get; set; }
    public uint ConfigManagerErrorCode { get; set; }
    public bool ConfigManagerUserConfig { get; set; }
    public string CreationClassName { get; set; }
    public string Description { get; set; }
    public uint DesignCapacity { get; set; }
    public ulong DesignVoltage { get; set; }
    public string DeviceID { get; set; }
    public bool ErrorCleared { get; set; }
    public string ErrorDescription { get; set; }
    public ushort EstimatedChargeRemaining { get; set; }
    public uint EstimatedRunTime { get; set; }
    public uint ExpectedBatteryLife { get; set; }
    public uint ExpectedLife { get; set; }
    public uint FullChargeCapacity { get; set; }
    public uint LastErrorCode { get; set; }
    public uint MaxRechargeTime { get; set; }
    public string Name { get; set; }
    public string PNPDeviceID { get; set; }
    public ushort[] PowerManagementCapabilities { get; set; }
    public bool PowerManagementSupported { get; set; }
    public string SmartBatteryVersion { get; set; }
    public string Status { get; set; }
    public ushort StatusInfo { get; set; }
    public string SystemCreationClassName { get; set; }
    public string SystemName { get; set; }
    public uint TimeOnBattery { get; set; }
    public uint TimeToFullCharge { get; set; }
}