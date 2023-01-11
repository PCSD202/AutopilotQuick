#region

using ORMi;

#endregion

namespace AutopilotQuick.WMI
{
    [WMIClass("Win32_ComputerSystem")]
    public class ComputerSystem : WMIInstance
    {
        public string Model { get; set; }
    }
}
