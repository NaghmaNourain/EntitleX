using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Retry;

public sealed class EfRetryClaimStore : IRetryClaimStore
{
    private readonly EntitleXDbContext _db;

    public EfRetryClaimStore(EntitleXDbContext db)
    {
        _db = db;
    }

    public async Task<RetryIdempotencyLookup?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.ApiIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

        return record is null
            ? null
            : new RetryIdempotencyLookup(
                record.IdempotencyKey,
                record.SourceRequestId,
                record.RetryRequestId,
                record.CreatedAtUtc);
    }

    public async Task<bool> TryClaimItemsAsync(
        RequestId sourceRequestId,
        RequestId retryRequestId,
        string idempotencyKey,
        IReadOnlyList<LogicalOperationId> logicalOperationIds,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (logicalOperationIds.Count == 0)
        {
            return false;
        }

        var distinct = logicalOperationIds.Distinct().ToList();

        var idempotencyTaken = await _db.ApiIdempotencyRecords
            .AsNoTracking()
            .AnyAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (idempotencyTaken)
        {
            return false;
        }

        var existingClaims = await _db.RetryItemClaims
            .AsNoTracking()
            .Where(x => x.SourceRequestId == sourceRequestId)
            .Select(x => x.LogicalOperationId)
            .ToListAsync(cancellationToken);

        if (distinct.Any(id => existingClaims.Contains(id)))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        _db.ApiIdempotencyRecords.Add(new ApiIdempotencyRecord(
            idempotencyKey,
            sourceRequestId,
            retryRequestId,
            notes,
            now));

        foreach (var logicalId in distinct)
        {
            _db.RetryItemClaims.Add(new RetryItemClaim(
                sourceRequestId,
                logicalId,
                retryRequestId,
                idempotencyKey,
                now));
        }

        // Persisted with the ambient unit of work (short PG txn). Unique indexes
        // remain the last line of defense under concurrent dual callers.
        return true;
    }
}
