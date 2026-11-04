namespace EntitleX.Application.EntitlementAggregation.Models;

/// <summary>Provider-normalized entitlement view used by the control plane.</summary>
public sealed record NormalizedEntitlementDto(
    string EntitlementId,
    string Provider,
    string CustomerId,
    string ProductCode,
    string? ProductGroup,
    string DisplayName,
    DateTimeOffset ExpiryUtc,
    DateTimeOffset? UpdatedAtUtc);
