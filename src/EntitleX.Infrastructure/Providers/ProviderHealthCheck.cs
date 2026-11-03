using EntitleX.Application.EntitlementAggregation.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EntitleX.Infrastructure.Providers;

public sealed class ProviderHealthCheck : IHealthCheck
{
    private readonly IProviderEntitlementClient _client;

    public ProviderHealthCheck(IProviderEntitlementClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthy = await _client.CheckHealthAsync(cancellationToken);
            return healthy
                ? HealthCheckResult.Healthy($"{_client.ProviderName} is reachable.")
                : HealthCheckResult.Unhealthy($"{_client.ProviderName} returned a non-success status.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{_client.ProviderName} health check failed.", ex);
        }
    }
}
