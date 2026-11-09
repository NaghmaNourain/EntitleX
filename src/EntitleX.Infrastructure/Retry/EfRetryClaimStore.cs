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

        // Derive the active set from the change tracker. ReleaseClaims loads every
        // claim for this source; AsNoTracking would still see Deleted rows that
        // have not been flushed yet and incorrectly block reclaim.
        await EnsureSourceClaimsTrackedAsync(sourceRequestId, cancellationToken);

        var activeClaimedIds = _db.ChangeTracker.Entries<RetryItemClaim>()
            .Where(e => e.Entity.SourceRequestId.Equals(sourceRequestId)
                        && e.State is not EntityState.Deleted)
            .Select(e => e.Entity.LogicalOperationId)
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
        await EnsureSourceClaimsTrackedAsync(sourceRequestId, cancellationToken);

        var relevant = _db.ChangeTracker.Entries<RetryItemClaim>()
            .Where(e => e.State is not EntityState.Deleted
                        && e.Entity.SourceRequestId.Equals(sourceRequestId)
                        && logicalOperationIds.Any(l => l.Equals(e.Entity.LogicalOperationId)))
            .Select(e => e.Entity)
            .ToList();

        if (relevant.Count == 0)
        {
            return;
        }

        var retryIds = relevant.Select(c => c.RetryRequestId).Distinct().ToList();
        var terminalSet = await ResolveTerminalRetryIdsAsync(retryIds, cancellationToken);
        if (terminalSet.Count == 0)
        {
            return;
        }

        foreach (var claim in relevant.Where(c => terminalSet.Contains(c.RetryRequestId)))
        {
            _db.RetryItemClaims.Remove(claim);
        }
    }

    private async Task EnsureSourceClaimsTrackedAsync(
        RequestId sourceRequestId,
        CancellationToken cancellationToken)
    {
        var alreadyTracked = _db.ChangeTracker.Entries<RetryItemClaim>()
            .Any(e => e.Entity.SourceRequestId.Equals(sourceRequestId));
        if (alreadyTracked)
        {
            return;
        }

        await _db.RetryItemClaims
            .Where(x => x.SourceRequestId == sourceRequestId)
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<RequestId>> ResolveTerminalRetryIdsAsync(
        IReadOnlyList<RequestId> retryIds,
        CancellationToken cancellationToken)
    {
        var terminal = new HashSet<RequestId>();
        var unresolved = new List<RequestId>();

        foreach (var retryId in retryIds)
        {
            var tracked = _db.LicenseExtensionRequests.Local
                .FirstOrDefault(r => r.Id.Equals(retryId));
            if (tracked is not null)
            {
                if (IsTerminalFailure(tracked.Status))
                {
                    terminal.Add(retryId);
                }

                continue;
            }

            unresolved.Add(retryId);
        }

        if (unresolved.Count == 0)
        {
            return terminal;
        }

        // Status-filtered query + in-memory Id match — avoids flaky value-object
        // Contains/PK translation on EF InMemory.
        var candidates = await _db.LicenseExtensionRequests
            .AsNoTracking()
            .Where(r => r.Status == LicenseExtensionRequestStatus.Failed
                        || r.Status == LicenseExtensionRequestStatus.Rejected)
            .ToListAsync(cancellationToken);

        foreach (var retryId in unresolved)
        {
            if (candidates.Any(r => r.Id.Equals(retryId)))
            {
                terminal.Add(retryId);
            }
        }

        return terminal;
    }

    private static bool IsTerminalFailure(LicenseExtensionRequestStatus status) =>
        status is LicenseExtensionRequestStatus.Failed
            or LicenseExtensionRequestStatus.Rejected;
}
