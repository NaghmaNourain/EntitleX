using CloudShield.Application.Abstractions;
using CloudShield.Domain.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace CloudShield.Infrastructure.Persistence.Repositories;

public sealed class CloudShieldEntitlementRepository : ICloudShieldEntitlementRepository
{
    private readonly CloudShieldDbContext _db;

    public CloudShieldEntitlementRepository(CloudShieldDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CloudShieldEntitlement>> FindByCustomerAccountIdAsync(
        string customerAccountId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Entitlements
            .AsNoTracking()
            .Where(e => e.CustomerAccountId == customerAccountId)
            .OrderBy(e => e.EntitlementKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<CloudShieldEntitlement?> GetByEntitlementKeyAsync(
        string entitlementKey,
        CancellationToken cancellationToken = default)
    {
        return await _db.Entitlements
            .FirstOrDefaultAsync(e => e.EntitlementKey == entitlementKey, cancellationToken);
    }
}
