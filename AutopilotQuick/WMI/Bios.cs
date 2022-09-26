using ORMi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
