using EntitleX.Application;
using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.Notifications;
using EntitleX.Application.Fulfillment;
using EntitleX.Application.Insights.Abstractions;
using EntitleX.Infrastructure.Audit;
using EntitleX.Infrastructure.Identity;
using EntitleX.Infrastructure.Insights;
using EntitleX.Infrastructure.Notifications;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Persistence.Repositories;
using EntitleX.Infrastructure.Providers;
using EntitleX.Infrastructure.Retry;
using EntitleX.Messaging;
using EntitleX.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;

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

        services.AddDbContext<EntitleXDbContext>((sp, options) =>
            ConfigurePlatformDbContext(sp, options, connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntitleXDbContext>());
        services.AddScoped<ILicenseExtensionRequestRepository, LicenseExtensionRequestRepository>();
        services.AddScoped<IApprovalCaseRepository, ApprovalCaseRepository>();
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxPoisonReplayStore, EfOutboxPoisonReplayStore>();
        services.AddScoped<IOutboxInsightStore, EfOutboxInsightStore>();
        services.AddScoped<OutboxClaimer>();
        services.AddScoped<OutboxPublisherService>();
        services.AddScoped<IConsumerIdempotencyStore, EfConsumerIdempotencyStore>();
        services.AddScoped<IRetryClaimStore, EfRetryClaimStore>();
        services.AddScoped<IApiIdempotencyStore, EfApiIdempotencyStore>();
        services.AddScoped<IAuditEventStore, EfAuditEventStore>();
        services.AddScoped<IApprovalReminderStore, EfApprovalReminderStore>();
        services.AddScoped<IExpirationNotificationStore, EfExpirationNotificationStore>();
        services.AddScoped<ILocalFulfillmentOutcomeStore, EfLocalFulfillmentOutcomeStore>();
        services.AddScoped<ProviderResultProcessor>();
        services.AddDomainEventDispatch();

        services.AddEntitleXMessaging(configuration);
        BindFulfillmentEnqueueOptions(services, configuration);
        services.AddHostedService<Observability.BacklogMetricsProbe>();
        services.AddHostedService<Observability.SqsDepthProbeHostedService>();

        services.AddProviderHttpClients(configuration);
        services.AddAiAssist(configuration);

        services.AddTransient<IProviderEntitlementClient, CloudShieldEntitlementClient>();
        services.AddTransient<IProviderEntitlementClient, NetworkGuardEntitlementClient>();
        services.AddTransient<IProviderOperationStatusClient, CloudShieldOperationStatusClient>();
        services.AddTransient<IProviderOperationStatusClient, NetworkGuardOperationStatusClient>();

        services.AddEntitleXAuthentication(configuration, environment);
        services.AddEntitleXEmail(configuration);
        services.AddScheduledNotificationJobs(configuration);
        services.Configure<Application.Notifications.ReconciliationAgingOptions>(
            configuration.GetSection(Application.Notifications.ReconciliationAgingOptions.SectionName));

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

        services.AddDbContext<EntitleXDbContext>((sp, options) =>
            ConfigurePlatformDbContext(sp, options, connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntitleXDbContext>());
        services.AddScoped<ILicenseExtensionRequestRepository, LicenseExtensionRequestRepository>();
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxPoisonReplayStore, EfOutboxPoisonReplayStore>();
        services.AddScoped<OutboxClaimer>();
        services.AddScoped<OutboxPublisherService>();
        services.AddScoped<IConsumerIdempotencyStore, EfConsumerIdempotencyStore>();
        services.AddScoped<IAuditEventStore, EfAuditEventStore>();
        services.AddScoped<IApprovalReminderStore, EfApprovalReminderStore>();
        services.AddScoped<IExpirationNotificationStore, EfExpirationNotificationStore>();
        services.AddScoped<ILocalFulfillmentOutcomeStore, EfLocalFulfillmentOutcomeStore>();
        services.AddScoped<ProviderResultProcessor>();
        services.AddDomainEventDispatch();
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

    private static void ConfigurePlatformDbContext(
        IServiceProvider sp,
        DbContextOptionsBuilder options,
        string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
            npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
            npgsql.MaxBatchSize(100);
            npgsql.CommandTimeout(60);
        });
        options.AddInterceptors(sp.GetRequiredService<DomainEventSaveChangesInterceptor>());
    }

    private static void BindFulfillmentEnqueueOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // Application-owned enqueue options — bound from Messaging:* without referencing adapters.
        services.Configure<FulfillmentEnqueueOptions>(options =>
        {
            var preferKafka = configuration["Messaging:Fulfillment:PreferDirectKafka"];
            if (bool.TryParse(preferKafka, out var prefer))
            {
                options.PreferDirectKafka = prefer;
            }

            var queue = configuration["Messaging:Sqs:FulfillmentQueueName"];
            if (!string.IsNullOrWhiteSpace(queue))
            {
                options.FulfillmentQueueName = queue;
            }

            var csTopic = configuration["Messaging:Kafka:CloudShieldCommandsTopic"];
            if (!string.IsNullOrWhiteSpace(csTopic))
            {
                options.CloudShieldCommandsTopic = csTopic;
            }

            var ngTopic = configuration["Messaging:Kafka:NetworkGuardCommandsTopic"];
            if (!string.IsNullOrWhiteSpace(ngTopic))
            {
                options.NetworkGuardCommandsTopic = ngTopic;
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

        services.AddDbContext<EntitleXDbContext>((sp, options) =>
            ConfigurePlatformDbContext(sp, options, connectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EntitleXDbContext>());
        services.AddScoped<ILicenseExtensionRequestRepository, LicenseExtensionRequestRepository>();
        services.AddScoped<IApprovalCaseRepository, ApprovalCaseRepository>();
        services.AddScoped<ILicenseExtensionSagaRepository, LicenseExtensionSagaRepository>();
        services.AddScoped<IAuditEventStore, EfAuditEventStore>();
        services.AddScoped<IApprovalReminderStore, EfApprovalReminderStore>();
        services.AddScoped<IExpirationNotificationStore, EfExpirationNotificationStore>();
        services.AddDomainEventDispatch();

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

    private static IServiceCollection AddAiAssist(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AiAssistOptions>(configuration.GetSection(AiAssistOptions.SectionName));
        services.PostConfigure<AiAssistOptions>(options =>
        {
            var envKey = Environment.GetEnvironmentVariable("ENTITLEX_AI_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                options.ApiKey = envKey;
            }
        });

        services.AddSingleton<IAiAssistGate, OptionsAiAssistGate>();

        // IChatClient is registered only when AiAssist is enabled at startup.
        // When disabled, RequestInsightNarrator sees an empty IEnumerable<IChatClient> and stays on Fallback.
        var enabled = configuration.GetValue($"{AiAssistOptions.SectionName}:Enabled", false);
        if (enabled)
        {
            services.AddSingleton<IChatClient>(sp =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiAssistOptions>>().Value;
                var uri = AiAssistEndpointNormalizer.ToOllamaUri(options.BaseUrl);
                var model = string.IsNullOrWhiteSpace(options.Model) ? "llama3.2" : options.Model;
                var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds);

                var httpClient = new HttpClient
                {
                    BaseAddress = uri,
                    Timeout = timeout
                };

                IChatClient inner = new OllamaApiClient(httpClient, model);
                return inner
                    .AsBuilder()
                    .UseOpenTelemetry(configure: otel => otel.EnableSensitiveData = false)
                    .UseLogging()
                    .Build(sp);
            });
        }

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
