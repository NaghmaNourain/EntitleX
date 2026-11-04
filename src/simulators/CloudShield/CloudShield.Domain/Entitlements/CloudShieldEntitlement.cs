using CloudShield.Domain.Common;

namespace CloudShield.Domain.Entitlements;

/// <summary>
/// CloudShield-owned entitlement (provider DB). Product/group fields use
/// CloudShield naming (ProductCode / ProductGroup / CustomerAccountId).
/// </summary>
public sealed class CloudShieldEntitlement
{
    private CloudShieldEntitlement()
    {
        EntitlementKey = null!;
        CustomerAccountId = null!;
        ProductCode = null!;
        DisplayName = null!;
    }

    private CloudShieldEntitlement(
        Guid id,
        string entitlementKey,
        string customerAccountId,
        string productCode,
        string? productGroup,
        string displayName,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        EntitlementKey = entitlementKey;
        CustomerAccountId = customerAccountId;
        ProductCode = productCode;
        ProductGroup = productGroup;
        DisplayName = displayName;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>External opaque entitlement id returned to EntitleX (e.g. CS-ENT-1001).</summary>
    public string EntitlementKey { get; private set; }

    public string CustomerAccountId { get; private set; }

    public string ProductCode { get; private set; }

    public string? ProductGroup { get; private set; }

    public string DisplayName { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static CloudShieldEntitlement Create(
        string entitlementKey,
        string customerAccountId,
        string productCode,
        string? productGroup,
        string displayName,
        DateTimeOffset expiresAtUtc,
        Guid? id = null,
        DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(entitlementKey))
        {
            throw new DomainException("EntitlementKey is required.");
        }

        if (string.IsNullOrWhiteSpace(customerAccountId))
        {
            throw new DomainException("CustomerAccountId is required.");
        }

        if (string.IsNullOrWhiteSpace(productCode))
        {
            throw new DomainException("ProductCode is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("DisplayName is required.");
        }

        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        return new CloudShieldEntitlement(
            id ?? Guid.NewGuid(),
            entitlementKey.Trim(),
            customerAccountId.Trim(),
            productCode.Trim(),
            string.IsNullOrWhiteSpace(productGroup) ? null : productGroup.Trim(),
            displayName.Trim(),
            expiresAtUtc,
            now);
    }

    /// <summary>Extends expiry; new expiry must be strictly after the current value.</summary>
    public void ExtendExpiry(DateTimeOffset newExpiresAtUtc, DateTimeOffset? updatedAtUtc = null)
    {
        if (newExpiresAtUtc <= ExpiresAtUtc)
        {
            throw new DomainException(
                $"New expiry '{newExpiresAtUtc:O}' must be after current expiry '{ExpiresAtUtc:O}'.");
        }

        ExpiresAtUtc = newExpiresAtUtc;
        UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow;
    }
}
