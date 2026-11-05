using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EntitleX.BuildingBlocks.Observability;

public static class OpenTelemetryHostingExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing (OTLP → Jaeger) and metrics (Prometheus exporter).
    /// Uses W3C trace context propagation (OpenTelemetry default).
    /// </summary>
    public static IServiceCollection AddEntitleXOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string? serviceNameOverride = null,
        bool isAspNetCoreHost = true)
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var options = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>()
            ?? new OpenTelemetryOptions();

        if (!string.IsNullOrWhiteSpace(serviceNameOverride))
        {
            options.ServiceName = serviceNameOverride;
        }

        services.Configure<OpenTelemetryOptions>(configuration.GetSection(OpenTelemetryOptions.SectionName));
        services.PostConfigure<OpenTelemetryOptions>(o =>
        {
            if (!string.IsNullOrWhiteSpace(serviceNameOverride))
            {
                o.ServiceName = serviceNameOverride;
            }
        });

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: options.ServiceName, serviceVersion: "1.0.0");

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(options.ServiceName, serviceVersion: "1.0.0"));

        if (options.EnableTracing)
        {
            otel.WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(EntitleXActivitySources.MessagingName)
                    .AddSource(EntitleXActivitySources.SagaName)
                    .AddSource(EntitleXActivitySources.OutboxName)
                    .AddSource(EntitleXActivitySources.ReconciliationName)
                    .AddSource(EntitleXActivitySources.ProvidersName)
                    .AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(options.OtlpEndpoint);
                    });

                if (options.EnableAspNetCoreInstrumentation && isAspNetCoreHost)
                {
                    tracing.AddAspNetCoreInstrumentation(asp =>
                    {
                        asp.RecordException = true;
                        asp.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/metrics")
                            && !ctx.Request.Path.StartsWithSegments("/health");
                    });
                }

                if (options.EnableHttpClientInstrumentation)
                {
                    tracing.AddHttpClientInstrumentation(http =>
                    {
                        http.RecordException = true;
                    });
                }

                if (options.EnableEntityFrameworkInstrumentation)
                {
                    tracing.AddEntityFrameworkCoreInstrumentation(ef =>
                    {
                        ef.SetDbStatementForText = true;
                    });
                }
            });
        }

        if (options.EnableMetrics)
        {
            otel.WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddMeter(EntitleXMeters.MeterName)
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddMeter("System.Net.Http");

                if (options.EnableAspNetCoreInstrumentation && isAspNetCoreHost)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                if (options.EnableHttpClientInstrumentation)
                {
                    metrics.AddHttpClientInstrumentation();
                }

                if (options.EnableRuntimeInstrumentation)
                {
                    metrics.AddRuntimeInstrumentation();
                }

                if (isAspNetCoreHost && options.EnablePrometheusAspNetCore)
                {
                    metrics.AddPrometheusExporter();
                }
                else if (!string.IsNullOrWhiteSpace(options.PrometheusHttpListenerPrefixes))
                {
                    metrics.AddPrometheusHttpListener(listener =>
                    {
                        listener.UriPrefixes = options.PrometheusHttpListenerPrefixes
                            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    });
                }
            });
        }

        return services;
    }

    /// <summary>Maps Prometheus scrape endpoint at <c>/metrics</c> for AspNetCore hosts.</summary>
    public static IEndpointRouteBuilder MapEntitleXPrometheusScrapingEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPrometheusScrapingEndpoint();
        return endpoints;
    }
}
