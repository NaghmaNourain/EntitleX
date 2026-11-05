using EntitleX.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Infrastructure.Messaging;
using NetworkGuard.Infrastructure.Persistence;
using NetworkGuard.Infrastructure.Persistence.Repositories;

namespace NetworkGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNetworkGuardInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NetworkGuard")
            ?? throw new InvalidOperationException(
                "Connection string 'NetworkGuard' is not configured.");

        services.AddDbContext<NetworkGuardDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                npgsql.MigrationsAssembly(typeof(NetworkGuardDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<INetworkGuardUnitOfWork>(sp => sp.GetRequiredService<NetworkGuardDbContext>());
        services.AddScoped<INetworkGuardLicenseRepository, NetworkGuardLicenseRepository>();
        services.AddScoped<IAppliedLicenseOperationRepository, AppliedLicenseOperationRepository>();
        services.AddScoped<ILicenseExtensionApplier, LicenseExtensionApplier>();

        if (configuration.GetValue("Messaging:Enabled", defaultValue: true))
        {
            services.AddEntitleXMessaging(configuration);
            services.AddHostedService<ProviderCommandConsumer>();
        }

        return services;
    }
}
