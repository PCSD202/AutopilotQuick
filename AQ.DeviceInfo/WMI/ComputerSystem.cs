#region

using ORMi;

#endregion

namespace AQ.DeviceInfo.WMI;

[WMIClass("Win32_ComputerSystem")]
public class ComputerSystem : WMIInstance
{
    public string Model { get; set; }
}