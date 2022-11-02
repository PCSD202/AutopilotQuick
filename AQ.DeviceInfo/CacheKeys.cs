namespace AQ.DeviceInfo;

internal static class CacheKeys
{
    private static string GenerateNewID(string Name)
    {
        return $"AQ.DeviceInfo.{Name}";
    }

    public static readonly string WmiHelper = GenerateNewID(nameof(WmiHelper));
    public static readonly string WmiHelperRoot = GenerateNewID(nameof(WmiHelperRoot));
    public static readonly string Win32_ComputerSystem = GenerateNewID(nameof(Win32_ComputerSystem));
    public static readonly string Win32_Bios = GenerateNewID(nameof(Win32_Bios));
    
    public static readonly string BatteryStaticData = GenerateNewID(nameof(BatteryStaticData));
    public static readonly string BatteryFullChargedCapacity = GenerateNewID(nameof(BatteryFullChargedCapacity));
    public static readonly string Battery = GenerateNewID(nameof(Battery));
}