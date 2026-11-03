using CloudShield.Application.Entitlements;
using CloudShield.Application.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShield.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudShieldApplication(this IServiceCollection services)
    {
        services.AddScoped<EntitlementQueries>();
        services.AddScoped<OperationQueries>();
        return services;
    }
}
