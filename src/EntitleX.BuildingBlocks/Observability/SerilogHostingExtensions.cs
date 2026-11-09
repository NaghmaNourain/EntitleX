using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace EntitleX.BuildingBlocks.Observability;

public static class SerilogHostingExtensions
{
    /// <summary>
    /// Structured console Serilog for worker / lambda hosts (shared enrichers).
    /// AWS path: keep the same property shape and add a CloudWatch Logs sink in deployment
    /// (see docs/observability.md).
    /// </summary>
    public static IServiceCollection AddEntitleXSerilog(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        services.AddSerilog((_, loggerConfiguration) =>
            ApplySharedEnrichers(loggerConfiguration, configuration, serviceName)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.Console());

        return services;
    }

    public static IHostBuilder UseEntitleXSerilog(this IHostBuilder host, string serviceName) =>
        host.UseSerilog((context, services, configuration) =>
            ApplySharedEnrichers(configuration, context.Configuration, serviceName)
                .ReadFrom.Services(services)
                .WriteTo.Console());

    /// <summary>
    /// Shared enrichers for host-local <c>UseSerilog</c> lambdas (Api / simulators).
    /// </summary>
    public static LoggerConfiguration ApplySharedEnrichers(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        string serviceName) =>
        loggerConfiguration
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.With(new ActivityTraceEnricher());
}
