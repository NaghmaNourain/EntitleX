using CloudShield.Domain.Entitlements;

namespace CloudShield.Application.Abstractions;

public interface ICloudShieldEntitlementRepository
{
    Task<IReadOnlyList<CloudShieldEntitlement>> FindByCustomerAccountIdAsync(
        string customerAccountId,
        CancellationToken cancellationToken = default);

    Task<CloudShieldEntitlement?> GetByEntitlementKeyAsync(
        string entitlementKey,
        CancellationToken cancellationToken = default);
}
