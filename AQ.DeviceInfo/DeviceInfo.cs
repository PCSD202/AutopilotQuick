using AQ.DeviceInfo.WMI;
using LazyCache;
using ORMi;

namespace AQ.DeviceInfo;

public static class DeviceInfo
{
    private static IAppCache _appCache = new CachingService();

    public static void ConfigureCache(IAppCache cache)
    {
        _appCache = cache;
    }

    private static WMIHelper _wmiHelper
    {
        get
        {
            WMIHelper CreateNewWmiHelper() => new("root\\CimV2");
            var cachedHelper = _appCache.GetOrAdd(CacheKeys.WmiHelper, CreateNewWmiHelper);
            return cachedHelper;
        }
    }

    private static ComputerSystem Win32_ComputerSystem
    {
        get
        {
            ComputerSystem CreateComputerSystem() => _wmiHelper.QueryFirstOrDefault<ComputerSystem>();
            return _appCache.GetOrAdd(CacheKeys.Win32_ComputerSystem, CreateComputerSystem);
        }
    }

    private static Bios Win32_Bios
    {
        get
        {
            Bios CreateBios() => _wmiHelper.QueryFirstOrDefault<Bios>();
            return _appCache.GetOrAdd(CacheKeys.Win32_Bios, CreateBios);
        }
    }

    public static string ServiceTag => Win32_Bios.SerialNumber;
    public static string BiosVersion => Win32_Bios.BIOSVersion;
    
    public static string DeviceModel => Win32_ComputerSystem.Model;
    
}