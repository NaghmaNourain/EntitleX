using EntitleX.Application;
using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Infrastructure.Identity;
using EntitleX.Infrastructure.Notifications;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Persistence.Repositories;
using EntitleX.Infrastructure.Providers;
using EntitleX.Infrastructure.Retry;
using EntitleX.Messaging;
using EntitleX.Messaging.Abstractions;
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
        services.AddScoped<IApprovalCaseRepository, ApprovalCaseRepository>();
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<OutboxClaimer>();
        services.AddScoped<OutboxPublisherService>();
        services.AddScoped<IConsumerIdempotencyStore, EfConsumerIdempotencyStore>();
        services.AddScoped<IRetryClaimStore, EfRetryClaimStore>();
        services.AddScoped<ProviderResultProcessor>();

        services.AddEntitleXMessaging(configuration);

        services.AddProviderHttpClients(configuration);

        services.AddTransient<IProviderEntitlementClient, CloudShieldEntitlementClient>();
        services.AddTransient<IProviderEntitlementClient, NetworkGuardEntitlementClient>();

        services.AddEntitleXAuthentication(configuration);
        services.AddEntitleXEmail(configuration);

        services.AddHealthChecks()
            .Add(CreateProviderRegistration("cloudshield", "CloudShield"))
            .Add(CreateProviderRegistration("networkguard", "NetworkGuard"));

        return services;
    }

    /// <summary>
    /// Worker-friendly registration: platform DB + messaging transports without HTTP auth/email.
    /// </summary>
    public static IServiceCollection AddInfrastructureForWorkers(
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
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<OutboxClaimer>();
        services.AddScoped<OutboxPublisherService>();
        services.AddScoped<IConsumerIdempotencyStore, EfConsumerIdempotencyStore>();
        services.AddScoped<ProviderResultProcessor>();
        services.AddSagaOrchestration();
        services.AddEntitleXMessaging(configuration);
        return services;
    }

    public static IServiceCollection AddEntitleXEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        var provider = configuration.GetValue<string>($"{EmailOptions.SectionName}:Provider")
            ?? "Smtp";

        if (string.Equals(provider, "Ses", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SesEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        services.AddScoped<IApprovalNotificationService, ApprovalNotificationService>();
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
