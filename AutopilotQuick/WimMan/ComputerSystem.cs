using ORMi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutopilotQuick
{
    [WMIClass("Win32_ComputerSystem")]
    public class ComputerSystem : WMIInstance
    {
        public string Model { get; set; }
    }
}
