using NetworkGuard.Domain.Common;

namespace NetworkGuard.Domain.Licenses;

/// <summary>
/// NetworkGuard-owned license (provider DB). Uses tenant/SKU/group naming
/// distinct from CloudShield's account/product model.
/// </summary>
public sealed class NetworkGuardLicense
{
    private NetworkGuardLicense()
    {
        LicenseId = null!;
        TenantExternalId = null!;
        SkuCode = null!;
        DisplayName = null!;
    }

    private NetworkGuardLicense(
        Guid id,
        string licenseId,
        string tenantExternalId,
        string skuCode,
        string? licenseGroup,
        string displayName,
        DateTimeOffset validUntilUtc,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        LicenseId = licenseId;
        TenantExternalId = tenantExternalId;
        SkuCode = skuCode;
        LicenseGroup = licenseGroup;
        DisplayName = displayName;
        ValidUntilUtc = validUntilUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>External opaque license id (e.g. NG-LIC-9001).</summary>
    public string LicenseId { get; private set; }

    public string TenantExternalId { get; private set; }

    public string SkuCode { get; private set; }

    public string? LicenseGroup { get; private set; }

    public string DisplayName { get; private set; }

    public DateTimeOffset ValidUntilUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NetworkGuardLicense Create(
        string licenseId,
        string tenantExternalId,
        string skuCode,
        string? licenseGroup,
        string displayName,
        DateTimeOffset validUntilUtc,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(licenseId))
        {
            throw new DomainException("LicenseId is required.");
        }

        if (string.IsNullOrWhiteSpace(tenantExternalId))
        {
            throw new DomainException("TenantExternalId is required.");
        }

        if (string.IsNullOrWhiteSpace(skuCode))
        {
            throw new DomainException("SkuCode is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("DisplayName is required.");
        }

        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        return new NetworkGuardLicense(
            id ?? Guid.NewGuid(),
            licenseId.Trim(),
            tenantExternalId.Trim(),
            skuCode.Trim(),
            string.IsNullOrWhiteSpace(licenseGroup) ? null : licenseGroup.Trim(),
            displayName.Trim(),
            validUntilUtc,
            now);
    }

    public void ExtendValidity(DateTimeOffset newValidUntilUtc, DateTimeOffset? updatedAtUtc = null)
    {
        if (newValidUntilUtc <= ValidUntilUtc)
        {
            throw new DomainException(
                $"New validity '{newValidUntilUtc:O}' must be after current '{ValidUntilUtc:O}'.");
        }

        ValidUntilUtc = newValidUntilUtc;
        UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow;
    }
}
