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
        services.AddSerilog((_, loggerConfiguration) => loggerConfiguration
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .WriteTo.Console());

        return services;
    }

    public static IHostBuilder UseEntitleXSerilog(this IHostBuilder host, string serviceName) =>
        host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .WriteTo.Console());
}
