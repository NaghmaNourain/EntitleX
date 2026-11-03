using Microsoft.Extensions.DependencyInjection;

namespace CloudShield.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudShieldApplication(this IServiceCollection services)
    {
        return services;
    }
}
