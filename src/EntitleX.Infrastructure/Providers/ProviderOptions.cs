namespace EntitleX.Infrastructure.Providers;

public sealed class ProviderOptions
{
    public const string SectionName = "Providers";

    public ProviderEndpointOptions CloudShield { get; set; } = new();

    public ProviderEndpointOptions NetworkGuard { get; set; } = new();
}

public sealed class ProviderEndpointOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret sent as <c>X-Api-Key</c> to vendor simulators.
    /// Must match <c>SimulatorAuth:ApiKey</c> on CloudShield / NetworkGuard.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Overall attempt timeout for a single HTTP call.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public int RetryCount { get; set; } = 3;

    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(15);

    public int BulkheadMaxParallelization { get; set; } = 32;

    public int BulkheadQueueLimit { get; set; } = 64;
}
