using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Retry;

public sealed class EfApiIdempotencyStore : IApiIdempotencyStore
{
    private readonly EntitleXDbContext _db;

    public EfApiIdempotencyStore(EntitleXDbContext db)
    {
        _db = db;
    }

    public async Task<ApiIdempotencyLookup?> FindAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.HttpWriteIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

        return record is null
            ? null
            : new ApiIdempotencyLookup(
                record.IdempotencyKey,
                record.Operation,
                record.ActorUserId,
                record.RequestFingerprint,
                record.ResourceId,
                record.CreatedAtUtc);
    }

    public async Task<bool> TryRecordAsync(
        string idempotencyKey,
        string operation,
        string actorUserId,
        string requestFingerprint,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var taken =
            _db.HttpWriteIdempotencyRecords.Local.Any(x => x.IdempotencyKey == idempotencyKey)
            || await _db.HttpWriteIdempotencyRecords
                .AsNoTracking()
                .AnyAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);

        if (taken)
        {
            return false;
        }

        _db.HttpWriteIdempotencyRecords.Add(new HttpWriteIdempotencyRecord(
            idempotencyKey,
            operation,
            actorUserId,
            requestFingerprint,
            resourceId,
            DateTimeOffset.UtcNow));

        return true;
    }
}
