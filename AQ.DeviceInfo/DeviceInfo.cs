#region

using System.Runtime.InteropServices;
using AQ.DeviceInfo.WMI;
using LazyCache;
using ORMi;

#endregion

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
    
    private static WMIHelper _wmiHelperRoot
    {
        get
        {
            WMIHelper CreateNewWmiHelper() => new("ROOT\\WMI");
            var cachedHelper = _appCache.GetOrAdd(CacheKeys.WmiHelperRoot, CreateNewWmiHelper);
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
    
    private static Battery? Win32_Battery
    {
        get
        {
            try
            {
                var result = _wmiHelper.Query<Battery>();
                if (result is null) return null;
                var batteryList = result.ToList();
                return batteryList.Any() ? batteryList.First() : null;
            }
            catch (COMException e)
            {
                return null;
            }
        }
    }
    
    private static BatteryStaticData? batteryStaticData
    {
        get
        {
            try
            {
                var BSD = _wmiHelperRoot.Query<BatteryStaticData>();
                if (BSD is null) return null;
                var batteryList = BSD.ToList();
                return batteryList.Any() ? batteryList.First() : null;
            }
            catch (COMException e)
            {
                return null;
            }
            
        }
    }
    
    private static BatteryFullChargedCapacity? batteryFullChargedCapacity
    {
        get
        {
            try
            {
                var BFCC = _wmiHelperRoot.Query<BatteryFullChargedCapacity>();
                if (BFCC is null) return null;
                var batteryList = BFCC.ToList();
                return batteryList.Any() ? batteryList.First() : null;
            }
            catch (COMException e)
            {
                return null;
            }
            
        }
    }

    public static string ServiceTag => Win32_Bios.SerialNumber;
    public static string BiosVersion => Win32_Bios.BIOSVersion;
    
    public static string DeviceModel => Win32_ComputerSystem.Model;

    public static string SystemSKUNumber => Win32_ComputerSystem.SystemSKUNumber;
    
    public static uint BatteryHealth
    {
        get
        {
            try
            {
                if (batteryStaticData is null || batteryFullChargedCapacity is null || Win32_Battery is null)
                {
                    return 0;
                }

                return (uint)(((double)batteryFullChargedCapacity.FullChargedCapacity /
                               batteryStaticData.DesignedCapacity) * 100);
            }
            catch (Exception e)
            {
                return 0;
            }
            
        }
    }

    public static bool BatteryConnected => Win32_Battery is not null;
}