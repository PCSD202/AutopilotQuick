using Microsoft.Extensions.DependencyInjection;

namespace AQ.Connectivity;

public static class ConnectivityServiceExtensions
{
    public static IServiceCollection AddConnectivityService(this IServiceCollection services, Action<ConnectivityServiceOptions> options)
    {
        //Configuration
        services.AddOptions<ConnectivityServiceOptions>().Configure(options);

        //Device identifier service
        services.AddSingleton<ConnectivityService>();
        
        return services;
    }
}