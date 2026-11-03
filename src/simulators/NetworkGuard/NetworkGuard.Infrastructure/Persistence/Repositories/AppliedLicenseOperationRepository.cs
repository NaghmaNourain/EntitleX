using EntitleX.BuildingBlocks.Ids;
using Microsoft.EntityFrameworkCore;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Domain.Operations;

namespace NetworkGuard.Infrastructure.Persistence.Repositories;

public sealed class AppliedLicenseOperationRepository : IAppliedLicenseOperationRepository
{
    private readonly NetworkGuardDbContext _db;

    public AppliedLicenseOperationRepository(NetworkGuardDbContext db)
    {
        _db = db;
    }

    public async Task<AppliedLicenseOperation?> GetByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        CancellationToken cancellationToken = default)
    {
        return await _db.AppliedLicenseOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                o => o.LogicalOperationId == logicalOperationId,
                cancellationToken);
    }

    public Task AddAsync(AppliedLicenseOperation operation, CancellationToken cancellationToken = default)
    {
        _db.AppliedLicenseOperations.Add(operation);
        return Task.CompletedTask;
    }
}
