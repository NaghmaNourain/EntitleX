using Microsoft.EntityFrameworkCore;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Domain.Licenses;

namespace NetworkGuard.Infrastructure.Persistence.Repositories;

public sealed class NetworkGuardLicenseRepository : INetworkGuardLicenseRepository
{
    private readonly NetworkGuardDbContext _db;

    public NetworkGuardLicenseRepository(NetworkGuardDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<NetworkGuardLicense>> FindByTenantExternalIdAsync(
        string tenantExternalId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Licenses
            .AsNoTracking()
            .Where(e => e.TenantExternalId == tenantExternalId)
            .OrderBy(e => e.LicenseId)
            .ToListAsync(cancellationToken);
    }

    public async Task<NetworkGuardLicense?> GetByLicenseIdAsync(
        string licenseId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Licenses
            .FirstOrDefaultAsync(e => e.LicenseId == licenseId, cancellationToken);
    }
}
