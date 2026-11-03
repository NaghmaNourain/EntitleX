namespace EntitleX.Application.EntitlementAggregation.Models;

/// <summary>
/// Partial aggregation result: successful provider contributions are included
/// even when sibling providers fail.
/// </summary>
public sealed record CustomerEntitlementAggregationDto(
    string CustomerId,
    string CorrelationId,
    IReadOnlyList<NormalizedEntitlementDto> Entitlements,
    IReadOnlyList<ProviderContributionDto> Providers,
    bool IsPartial);
