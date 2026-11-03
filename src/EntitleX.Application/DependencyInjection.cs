using Microsoft.Extensions.DependencyInjection;

namespace EntitleX.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application-layer services. Phase 1 is a no-op placeholder.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
