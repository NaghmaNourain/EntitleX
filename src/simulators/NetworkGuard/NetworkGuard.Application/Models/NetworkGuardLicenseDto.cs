namespace NetworkGuard.Application.Models;

public sealed record NetworkGuardLicenseDto(
    string LicenseId,
    string TenantExternalId,
    string SkuCode,
    string? LicenseGroup,
    string DisplayName,
    DateTimeOffset ValidUntilUtc,
    DateTimeOffset UpdatedAtUtc);
