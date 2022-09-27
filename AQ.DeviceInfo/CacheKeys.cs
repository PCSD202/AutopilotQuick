namespace AQ.DeviceInfo;

internal static class CacheKeys
{
    private static string GenerateNewID(string Name)
    {
        return $"AQ.DeviceInfo.{Name}";
    }

    public static readonly string WmiHelper = GenerateNewID(nameof(WmiHelper));
    public static readonly string Win32_ComputerSystem = GenerateNewID(nameof(Win32_ComputerSystem));
    public static readonly string Win32_Bios = GenerateNewID(nameof(Win32_Bios));
}