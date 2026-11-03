using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Persistence.Repositories;
using EntitleX.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EntitleX.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("EntitleXPlatform")
            ?? throw new InvalidOperationException(
                "Connection string 'EntitleXPlatform' is not configured.");

        services.AddDbContext<EntitleXDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntitleXDbContext>());
        services.AddScoped<ILicenseExtensionRequestRepository, LicenseExtensionRequestRepository>();

        services.AddProviderHttpClients(configuration);

        services.AddTransient<IProviderEntitlementClient, CloudShieldEntitlementClient>();
        services.AddTransient<IProviderEntitlementClient, NetworkGuardEntitlementClient>();

        services.AddHealthChecks()
            .Add(CreateProviderRegistration("cloudshield", "CloudShield"))
            .Add(CreateProviderRegistration("networkguard", "NetworkGuard"));

        return services;
    }

    private static HealthCheckRegistration CreateProviderRegistration(
        string name,
        string providerName) =>
        new(
            name,
            sp =>
            {
                var client = sp.GetServices<IProviderEntitlementClient>()
                    .Single(c => c.ProviderName == providerName);
                return new ProviderHealthCheck(client);
            },
            failureStatus: HealthStatus.Unhealthy,
            tags: ["provider", providerName.ToLowerInvariant()]);
}
