using NetworkGuard.Application.Abstractions;
using NetworkGuard.Application.Models;
using NetworkGuard.Domain.Licenses;

namespace NetworkGuard.Application.Licenses;

public sealed class LicenseQueries
{
    private readonly INetworkGuardLicenseRepository _repository;

    public LicenseQueries(INetworkGuardLicenseRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<NetworkGuardLicenseDto>> DiscoverByTenantAsync(
        string tenantExternalId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _repository.FindByTenantExternalIdAsync(tenantExternalId, cancellationToken);
        return entities.Select(ToDto).ToList();
    }

    public async Task<NetworkGuardLicenseDto?> GetByLicenseIdAsync(
        string licenseId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByLicenseIdAsync(licenseId, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    private static NetworkGuardLicenseDto ToDto(NetworkGuardLicense e) =>
        new(
            e.LicenseId,
            e.TenantExternalId,
            e.SkuCode,
            e.LicenseGroup,
            e.DisplayName,
            e.ValidUntilUtc,
            e.UpdatedAtUtc);
}
