using CloudShield.Application.Abstractions;
using CloudShield.Infrastructure.Persistence;
using CloudShield.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShield.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudShieldInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CloudShield")
            ?? throw new InvalidOperationException(
                "Connection string 'CloudShield' is not configured.");

        services.AddDbContext<CloudShieldDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                npgsql.MigrationsAssembly(typeof(CloudShieldDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<ICloudShieldUnitOfWork>(sp => sp.GetRequiredService<CloudShieldDbContext>());
        services.AddScoped<ICloudShieldEntitlementRepository, CloudShieldEntitlementRepository>();
        services.AddScoped<IAppliedLicenseOperationRepository, AppliedLicenseOperationRepository>();
        services.AddScoped<ILicenseExtensionApplier, LicenseExtensionApplier>();

        return services;
    }
}
