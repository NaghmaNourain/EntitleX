using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Persistence.Repositories;

public sealed class LicenseExtensionRequestRepository : ILicenseExtensionRequestRepository
{
    private readonly EntitleXDbContext _dbContext;

    public LicenseExtensionRequestRepository(EntitleXDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(LicenseExtensionRequest request, CancellationToken cancellationToken = default)
    {
        // Up to MaxItems (1000): disable auto-detect during graph attach, then rely on
        // Npgsql MaxBatchSize for batched INSERTs (see DependencyInjection / docs/performance.md).
        var autoDetect = _dbContext.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            _dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
            await _dbContext.LicenseExtensionRequests.AddAsync(request, cancellationToken);
        }
        finally
        {
            _dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    public async Task<LicenseExtensionRequest?> GetByIdAsync(
        RequestId id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LicenseExtensionRequests
            .Include(x => x.Items)
            .Include(x => x.RetryHistory)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<bool> ExtensionNumberExistsAsync(
        string extensionNumber,
        CancellationToken cancellationToken = default)
    {
        // Existence check — no tracking, no Includes (avoid N+1 / tracker noise).
        return _dbContext.LicenseExtensionRequests
            .AsNoTracking()
            .AnyAsync(x => x.ExtensionNumber == extensionNumber, cancellationToken);
    }
}
