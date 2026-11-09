using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Saga;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Persistence.Repositories;

public sealed class LicenseExtensionSagaRepository : ILicenseExtensionSagaRepository
{
    private readonly EntitleXDbContext _dbContext;

    public LicenseExtensionSagaRepository(EntitleXDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(LicenseExtensionSaga saga, CancellationToken cancellationToken = default)
    {
        await _dbContext.LicenseExtensionSagas.AddAsync(saga, cancellationToken);
    }

    public async Task<LicenseExtensionSaga?> GetByRequestIdAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default)
    {
        // EF Core SQL queries do not return Added-but-unsaved entities. Create/auto-approve
        // starts a saga and dispatches fulfillment in the same unit of work before SaveChanges.
        var local = _dbContext.LicenseExtensionSagas.Local
            .FirstOrDefault(x => x.RequestId.Equals(requestId));
        if (local is not null)
        {
            return local;
        }

        return await _dbContext.LicenseExtensionSagas
            .Include(x => x.Transitions)
            .Include(x => x.ItemOutcomes)
            .SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
    }

    public Task<LicenseExtensionSaga?> GetByRequestIdReadOnlyAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default) =>
        _dbContext.LicenseExtensionSagas
            .AsNoTracking()
            .Include(x => x.Transitions)
            .Include(x => x.ItemOutcomes)
            .SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);

    public Task<LicenseExtensionSaga?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        _dbContext.LicenseExtensionSagas
            .Include(x => x.Transitions)
            .Include(x => x.ItemOutcomes)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LicenseExtensionSaga>> ListTimedOutAsync(
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LicenseExtensionSagas
            .Include(x => x.Transitions)
            .Include(x => x.ItemOutcomes)
            .Where(x =>
                x.TimeoutKind != SagaTimeoutKind.None &&
                x.TimeoutDeadlineUtc != null &&
                x.TimeoutDeadlineUtc <= nowUtc &&
                x.State != SagaState.Completed &&
                x.State != SagaState.Failed &&
                x.State != SagaState.Rejected)
            .OrderBy(x => x.TimeoutDeadlineUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LicenseExtensionSaga>> ListRequiringReconciliationAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LicenseExtensionSagas
            .Include(x => x.Transitions)
            .Include(x => x.ItemOutcomes)
            .Where(x =>
                x.State == SagaState.ReconciliationRequired
                || x.ItemOutcomes.Any(o =>
                    o.Status == SagaItemOutcomeStatus.Uncertain
                    || (o.Status == SagaItemOutcomeStatus.Pending
                        && (x.State == SagaState.Processing
                            || x.State == SagaState.QueuedForFulfillment
                            || x.State == SagaState.PartiallyCompleted))))
            .OrderBy(x => x.UpdatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
