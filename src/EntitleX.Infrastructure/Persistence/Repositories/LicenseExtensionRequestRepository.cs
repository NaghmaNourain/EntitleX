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
        await _dbContext.LicenseExtensionRequests.AddAsync(request, cancellationToken);
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
        return _dbContext.LicenseExtensionRequests
            .AnyAsync(x => x.ExtensionNumber == extensionNumber, cancellationToken);
    }
}
