using Microsoft.Extensions.DependencyInjection;

namespace EntitleX.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers infrastructure adapters. Phase 1 is a no-op placeholder
    /// (no EF Core, Kafka producers, or external clients yet).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        return services;
    }
}
