using Microsoft.Extensions.DependencyInjection;

namespace NetworkGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNetworkGuardApplication(this IServiceCollection services)
    {
        return services;
    }
}
