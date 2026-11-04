using CloudShield.Application.Abstractions;
using CloudShield.Domain.Operations;
using EntitleX.BuildingBlocks.Ids;
using Microsoft.EntityFrameworkCore;

namespace CloudShield.Infrastructure.Persistence.Repositories;

public sealed class AppliedLicenseOperationRepository : IAppliedLicenseOperationRepository
{
    private readonly CloudShieldDbContext _db;

    public AppliedLicenseOperationRepository(CloudShieldDbContext db)
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
