using EntitleX.Application.EntitlementAggregation.Models;

namespace EntitleX.Application.EntitlementAggregation.Abstractions;

public interface IProviderEntitlementClient
{
    string ProviderName { get; }

    /// <summary>Idempotent discovery GET against the provider simulator.</summary>
    Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
        string customerId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Idempotent readiness probe (typically GET /health).</summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
