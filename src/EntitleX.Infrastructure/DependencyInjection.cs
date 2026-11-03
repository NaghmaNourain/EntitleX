using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
