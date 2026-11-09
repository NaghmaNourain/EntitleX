using EntitleX.Application;
using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.Notifications;
using EntitleX.Application.Fulfillment;
using EntitleX.Infrastructure.Audit;
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
using Microsoft.Extensions.Hosting;

namespace EntitleX.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        var connectionString = configuration.GetConnectionString("EntitleXPlatform")
            ?? throw new InvalidOperationException(
                "Connection string 'EntitleXPlatform' is not configured.");

        services.AddDbContext<EntitleXDbContext>(options =>
        {
            // Npgsql pools connections by default (Max Pool Size=100). Tune via connection string
            // (Minimum Pool Size / Maximum Pool Size / Timeout) — see docs/performance.md.
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
                // Batch up to 100 commands per round-trip (helps 1000-item request inserts).
                npgsql.MaxBatchSize(100);
                npgsql.CommandTimeout(60);
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
        services.AddScoped<IAuditEventStore, EfAuditEventStore>();
        services.AddScoped<IApprovalReminderStore, EfApprovalReminderStore>();
        services.AddScoped<IExpirationNotificationStore, EfExpirationNotificationStore>();
        services.AddScoped<ILocalFulfillmentOutcomeStore, EfLocalFulfillmentOutcomeStore>();
        services.AddScoped<ProviderResultProcessor>();

        services.AddEntitleXMessaging(configuration);
        BindFulfillmentEnqueueOptions(services, configuration);
        services.AddHostedService<Observability.BacklogMetricsProbe>();
        services.AddHostedService<Observability.SqsDepthProbeHostedService>();

        services.AddProviderHttpClients(configuration);

        services.AddTransient<IProviderEntitlementClient, CloudShieldEntitlementClient>();
        services.AddTransient<IProviderEntitlementClient, NetworkGuardEntitlementClient>();
        services.AddTransient<IProviderOperationStatusClient, CloudShieldOperationStatusClient>();
        services.AddTransient<IProviderOperationStatusClient, NetworkGuardOperationStatusClient>();

        services.AddEntitleXAuthentication(configuration, environment);
        services.AddEntitleXEmail(configuration);
        services.AddScheduledNotificationJobs(configuration);

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
                npgsql.MaxBatchSize(100);
                npgsql.CommandTimeout(60);
            });
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntitleXDbContext>());
        services.AddScoped<ILicenseExtensionRequestRepository, LicenseExtensionRequestRepository>();
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<OutboxClaimer>();
        services.AddScoped<OutboxPublisherService>();
        services.AddScoped<IConsumerIdempotencyStore, EfConsumerIdempotencyStore>();
        services.AddScoped<IAuditEventStore, EfAuditEventStore>();
        services.AddScoped<IApprovalReminderStore, EfApprovalReminderStore>();
        services.AddScoped<IExpirationNotificationStore, EfExpirationNotificationStore>();
        services.AddScoped<ILocalFulfillmentOutcomeStore, EfLocalFulfillmentOutcomeStore>();
        services.AddScoped<ProviderResultProcessor>();
        services.AddProviderHttpClients(configuration);
        services.AddTransient<IProviderOperationStatusClient, CloudShieldOperationStatusClient>();
        services.AddTransient<IProviderOperationStatusClient, NetworkGuardOperationStatusClient>();
        services.AddSagaOrchestration();
        services.AddEntitleXMessaging(configuration);
        BindFulfillmentEnqueueOptions(services, configuration);
        services.AddHostedService<Observability.BacklogMetricsProbe>();
        services.AddHostedService<Observability.SqsDepthProbeHostedService>();
        return services;
    }

    private static void BindFulfillmentEnqueueOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Application-owned enqueue options — bound from Messaging:Sqs without referencing adapters.
        services.Configure<FulfillmentEnqueueOptions>(options =>
        {
            var queue = configuration["Messaging:Sqs:FulfillmentQueueName"];
            if (!string.IsNullOrWhiteSpace(queue))
            {
                options.FulfillmentQueueName = queue;
            }
        });
    }

    /// <summary>
    /// Lambda / scheduled-job registration: platform DB + email (SMTP/Mailpit or SES) +
    /// provider discovery clients + reminder/expiration services. No JWT.
    /// </summary>
    public static IServiceCollection AddInfrastructureForLambdas(
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
                npgsql.MaxBatchSize(100);
                npgsql.CommandTimeout(60);
            });
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntitleXDbContext>());
        services.AddScoped<ILicenseExtensionRequestRepository, LicenseExtensionRequestRepository>();
        services.AddScoped<IApprovalCaseRepository, ApprovalCaseRepository>();
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IAuditEventStore, EfAuditEventStore>();
        services.AddScoped<IApprovalReminderStore, EfApprovalReminderStore>();
        services.AddScoped<IExpirationNotificationStore, EfExpirationNotificationStore>();

        services.AddProviderHttpClients(configuration);
        services.AddTransient<IProviderEntitlementClient, CloudShieldEntitlementClient>();
        services.AddTransient<IProviderEntitlementClient, NetworkGuardEntitlementClient>();

        services.AddEntitleXEmail(configuration);
        services.AddScheduledNotificationJobs(configuration);
        return services;
    }

    public static IServiceCollection AddEntitleXEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<EmailPortalOptions>(configuration.GetSection(EmailPortalOptions.SectionName));

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

    public static IServiceCollection AddScheduledNotificationJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ApprovalReminderOptions>(
            configuration.GetSection(ApprovalReminderOptions.SectionName));
        services.Configure<ExpirationNotificationOptions>(
            configuration.GetSection(ExpirationNotificationOptions.SectionName));
        services.AddScoped<IApprovalReminderService, ApprovalReminderService>();
        services.AddScoped<IExpirationNotificationService, ExpirationNotificationService>();
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
