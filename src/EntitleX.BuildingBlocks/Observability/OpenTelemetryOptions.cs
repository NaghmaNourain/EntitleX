namespace EntitleX.BuildingBlocks.Observability;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Service name reported to Jaeger / Prometheus.</summary>
    public string ServiceName { get; set; } = "EntitleX";

    /// <summary>OTLP endpoint (Jaeger all-in-one defaults to http://localhost:4317).</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>When true, AspNetCore hosts expose /metrics for Prometheus scrape.</summary>
    public bool EnablePrometheusAspNetCore { get; set; } = true;

    /// <summary>When set, worker hosts expose Prometheus via HttpListener (e.g. http://localhost:9180/metrics).</summary>
    public string? PrometheusHttpListenerPrefixes { get; set; }

    public bool EnableTracing { get; set; } = true;

    public bool EnableMetrics { get; set; } = true;

    public bool EnableAspNetCoreInstrumentation { get; set; } = true;

    public bool EnableHttpClientInstrumentation { get; set; } = true;

    public bool EnableEntityFrameworkInstrumentation { get; set; } = true;

    public bool EnableRuntimeInstrumentation { get; set; } = true;
}
