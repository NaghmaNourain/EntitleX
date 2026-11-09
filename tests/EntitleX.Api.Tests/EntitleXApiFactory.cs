using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Infrastructure.Observability;
using EntitleX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using Testcontainers.PostgreSql;

namespace EntitleX.Api.Tests;

public sealed class EntitleXApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "EntitleX-Api-Tests-Signing-Key-32chars!";

    private PostgreSqlContainer? _postgres;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("entitlex_platform")
            .WithUsername("entitlex")
            .WithPassword("entitlex")
            .Build();

        await _postgres.StartAsync();
        ConnectionString = _postgres.GetConnectionString();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        await Log.CloseAndFlushAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "EntitleXApiFactory.ConnectionString is empty; Testcontainers PostgreSQL was not started. " +
                "API integration tests require Docker.");
        }

        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EntitleXPlatform"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
                ["Jwt:Issuer"] = "EntitleX",
                ["Jwt:Audience"] = "EntitleX",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:EnableDevTokenEndpoint"] = "true",
                ["Jwt:AllowDevPlatformAdmin"] = "true",
                ["Oidc:Enabled"] = "false",
                ["Oidc:Authority"] = "",
                ["RateLimiting:Enabled"] = "false",
                ["Email:Provider"] = "Smtp",
                ["Email:Smtp:Host"] = "127.0.0.1",
                ["Email:Smtp:Port"] = "1025",
                ["Messaging:Sqs:ServiceUrl"] = "",
                ["Messaging:Kafka:BootstrapServers"] = "127.0.0.1:9092",
                ["OpenTelemetry:EnableTracing"] = "false",
                ["OpenTelemetry:EnableMetrics"] = "false",
                ["AiAssist:Enabled"] = "false",
                ["Providers:CloudShield:BaseUrl"] = "http://127.0.0.1:9/",
                ["Providers:NetworkGuard:BaseUrl"] = "http://127.0.0.1:9/"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<BacklogMetricsProbe>();
            services.RemoveAll<SqsDepthProbeHostedService>();

            services.RemoveAll<IProviderEntitlementClient>();
            services.AddTransient<IProviderEntitlementClient, StubCloudShieldClient>();
            services.AddTransient<IProviderEntitlementClient, StubNetworkGuardClient>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Program.cs assigns a Serilog bootstrap logger and UseSerilog freezes it.
        // Reset between host builds so a misconfigured parallel fixture cannot poison the run.
        Log.CloseAndFlush();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        return base.CreateHost(builder);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();
        await db.Database.MigrateAsync();
    }

    private sealed class StubCloudShieldClient : IProviderEntitlementClient
    {
        public string ProviderName => "CloudShield";

        public Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
            string customerId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NormalizedEntitlementDto>>(
            [
                new(
                    "cs-ent-1",
                    "CloudShield",
                    customerId,
                    "CS-PROD",
                    null,
                    "CloudShield Seat",
                    DateTimeOffset.Parse("2026-12-01T00:00:00Z"),
                    DateTimeOffset.UtcNow)
            ]);

        public Task<NormalizedEntitlementDto?> GetByEntitlementIdAsync(
            string entitlementId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalizedEntitlementDto?>(
                new(
                    entitlementId,
                    "CloudShield",
                    "CUST-1",
                    "CS-PROD",
                    null,
                    "CloudShield Seat",
                    DateTimeOffset.Parse("2026-12-01T00:00:00Z"),
                    DateTimeOffset.UtcNow));

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class StubNetworkGuardClient : IProviderEntitlementClient
    {
        public string ProviderName => "NetworkGuard";

        public Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
            string customerId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NormalizedEntitlementDto>>(
            [
                new(
                    "ng-ent-1",
                    "NetworkGuard",
                    customerId,
                    "NG-PROD",
                    null,
                    "NetworkGuard License",
                    DateTimeOffset.Parse("2026-12-01T00:00:00Z"),
                    DateTimeOffset.UtcNow)
            ]);

        public Task<NormalizedEntitlementDto?> GetByEntitlementIdAsync(
            string entitlementId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalizedEntitlementDto?>(
                new(
                    entitlementId,
                    "NetworkGuard",
                    "CUST-1",
                    "NG-PROD",
                    null,
                    "NetworkGuard License",
                    DateTimeOffset.Parse("2026-12-01T00:00:00Z"),
                    DateTimeOffset.UtcNow));

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
