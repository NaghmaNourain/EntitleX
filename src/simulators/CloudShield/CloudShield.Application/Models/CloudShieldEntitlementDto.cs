namespace CloudShield.Application.Models;

public sealed record CloudShieldEntitlementDto(
    string EntitlementKey,
    string CustomerAccountId,
    string ProductCode,
    string? ProductGroup,
    string DisplayName,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);
