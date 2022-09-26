using System;
using AutopilotQuick.WMI;
using LazyCache;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using ORMi;

namespace AutopilotQuick;

public static class DeviceInfoHelper
{
    
    private static readonly IAppCache _appCache = new CachingService();
    
    private static WMIHelper GetWmiHelper()
    {
        var wmiHelperFactory = () => new WMIHelper("root\\CimV2");
        var cachedHelper = _appCache.GetOrAdd("WMIHelper", wmiHelperFactory);
        return cachedHelper;
    }
    
    public static string DeviceModel
    {
        get
        {
            string DeviceModelFactory()
            {
                var helper = GetWmiHelper();
                var model = helper.QueryFirstOrDefault<ComputerSystem>().Model;
                return model;
            }
            var cachedModel = _appCache.GetOrAdd("DeviceModel", (Func<string>)DeviceModelFactory);
            return cachedModel ?? "UNKNOWN";
        }
    }

    public static string ServiceTag
    {
        get
        {
            string ServiceTagFactory()
            {
                var helper = GetWmiHelper();
                var serviceTag = helper.QueryFirstOrDefault<Bios>().SerialNumber;
                return serviceTag;
            }
            var serviceTag = _appCache.GetOrAdd("ServiceTag", (Func<string>)ServiceTagFactory);
            return serviceTag ?? "UNKNOWN";
        }
    }
    
    public static string BiosVersion
    {
        get
        {
            string BiosVersionFactory()
            {
                var helper = GetWmiHelper();
                var biosVersion = helper.QueryFirstOrDefault<Bios>().BIOSVersion;
                return biosVersion;
            }
            var BiosVersion = _appCache.GetOrAdd("BiosVersion", (Func<string>)BiosVersionFactory);
            return BiosVersion ?? "UNKNOWN";
        }
    }
}