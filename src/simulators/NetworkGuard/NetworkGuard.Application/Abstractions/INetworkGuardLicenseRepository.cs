using NetworkGuard.Domain.Licenses;

namespace NetworkGuard.Application.Abstractions;

public interface INetworkGuardLicenseRepository
{
    Task<IReadOnlyList<NetworkGuardLicense>> FindByTenantExternalIdAsync(
        string tenantExternalId,
        CancellationToken cancellationToken = default);

    Task<NetworkGuardLicense?> GetByLicenseIdAsync(
        string licenseId,
        CancellationToken cancellationToken = default);
}
