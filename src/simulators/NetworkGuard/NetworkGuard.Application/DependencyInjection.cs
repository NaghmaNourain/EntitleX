using Microsoft.Extensions.DependencyInjection;
using NetworkGuard.Application.Licenses;
using NetworkGuard.Application.Operations;

namespace NetworkGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNetworkGuardApplication(this IServiceCollection services)
    {
        services.AddScoped<LicenseQueries>();
        services.AddScoped<OperationQueries>();
        return services;
    }
}
