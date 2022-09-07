using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AQ.DeviceIdentifier;

public static class DeviceIdentifierServiceExtensions
{
    public static IServiceCollection AddDeviceIdentifier(this IServiceCollection services, Action<DeviceIdentifierServiceOptions> options)
    {
        //Configuration
        services.AddOptions<DeviceIdentifierServiceOptions>().Configure(options);
        
        //Filesystem
        services.AddSingleton<IFileSystem, FileSystem>();
        
        //Device identifier service
        services.AddSingleton<IDeviceIdentifierService, DeviceIdentifierService>();
        
        return services;
    }
}