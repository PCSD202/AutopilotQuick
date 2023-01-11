#region

using ORMi;

#endregion

namespace AutopilotQuick.WMI
{
    [WMIClass("Win32_Bios")]
    public class Bios : WMIInstance
    {
        public string SerialNumber { get; set; }
        
        [WMIProperty(Name = "SMBIOSBIOSVersion")]
        public string BIOSVersion { get; set; }
    }
}
