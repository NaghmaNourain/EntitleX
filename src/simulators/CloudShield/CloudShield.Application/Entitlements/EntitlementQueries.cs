using CloudShield.Application.Abstractions;
using CloudShield.Application.Models;
using CloudShield.Domain.Entitlements;

namespace CloudShield.Application.Entitlements;

public sealed class EntitlementQueries
{
    private readonly ICloudShieldEntitlementRepository _repository;

    public EntitlementQueries(ICloudShieldEntitlementRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CloudShieldEntitlementDto>> DiscoverByCustomerAsync(
        string customerAccountId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _repository.FindByCustomerAccountIdAsync(
            customerAccountId,
            cancellationToken);
        return entities.Select(ToDto).ToList();
    }

    public async Task<CloudShieldEntitlementDto?> GetByKeyAsync(
        string entitlementKey,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByEntitlementKeyAsync(entitlementKey, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    private static CloudShieldEntitlementDto ToDto(CloudShieldEntitlement e) =>
        new(
            e.EntitlementKey,
            e.CustomerAccountId,
            e.ProductCode,
            e.ProductGroup,
            e.DisplayName,
            e.ExpiresAtUtc,
            e.UpdatedAtUtc);
}
