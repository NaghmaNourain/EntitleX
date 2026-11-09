using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;
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

        // Include Local: prior TryClaim in the same scoped DbContext may have
        // staged rows that AsNoTracking queries will not see until SaveChanges.
        var idempotencyTaken =
            _db.ApiIdempotencyRecords.Local.Any(x => x.IdempotencyKey == idempotencyKey)
            || await _db.ApiIdempotencyRecords
                .AsNoTracking()
                .AnyAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (idempotencyTaken)
        {
            return false;
        }

        await ReleaseClaimsForTerminalRetriesAsync(
            sourceRequestId,
            distinct,
            cancellationToken);

        var existingClaims = await _db.RetryItemClaims
            .AsNoTracking()
            .Where(x => x.SourceRequestId == sourceRequestId)
            .Select(x => new { x.LogicalOperationId, x.RetryRequestId })
            .ToListAsync(cancellationToken);

        var activeClaimedIds = existingClaims
            .Select(x => x.LogicalOperationId)
            .Concat(
                _db.RetryItemClaims.Local
                    .Where(x => x.SourceRequestId == sourceRequestId
                        && _db.Entry(x).State != EntityState.Deleted)
                    .Select(x => x.LogicalOperationId))
            .ToHashSet();

        if (distinct.Any(activeClaimedIds.Contains))
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

    /// <summary>
    /// Releases claims whose child retry request is terminal Failed or Rejected
    /// so a later selective retry of the same LogicalOperationIds can proceed.
    /// </summary>
    private async Task ReleaseClaimsForTerminalRetriesAsync(
        RequestId sourceRequestId,
        IReadOnlyList<LogicalOperationId> logicalOperationIds,
        CancellationToken cancellationToken)
    {
        var claims = await _db.RetryItemClaims
            .Where(x => x.SourceRequestId == sourceRequestId)
            .ToListAsync(cancellationToken);

        var relevant = claims
            .Where(c => logicalOperationIds.Any(l => l.Equals(c.LogicalOperationId)))
            .ToList();

        if (relevant.Count == 0)
        {
            return;
        }

        var retryIds = relevant.Select(c => c.RetryRequestId).Distinct().ToList();
        var terminalStatuses = new[]
        {
            LicenseExtensionRequestStatus.Failed,
            LicenseExtensionRequestStatus.Rejected
        };

        var terminalRetryIds = await _db.LicenseExtensionRequests
            .AsNoTracking()
            .Where(r => retryIds.Contains(r.Id) && terminalStatuses.Contains(r.Status))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (terminalRetryIds.Count == 0)
        {
            return;
        }

        var terminalSet = terminalRetryIds.ToHashSet();
        foreach (var claim in relevant.Where(c => terminalSet.Contains(c.RetryRequestId)))
        {
            _db.RetryItemClaims.Remove(claim);
        }
    }
}
