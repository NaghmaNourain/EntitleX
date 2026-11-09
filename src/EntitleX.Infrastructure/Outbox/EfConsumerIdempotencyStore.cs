using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EntitleX.Infrastructure.Outbox;

public sealed class EfConsumerIdempotencyStore : IConsumerIdempotencyStore
{
    private readonly EntitleXDbContext _db;

    public EfConsumerIdempotencyStore(EntitleXDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryBeginAsync(
        string consumerName,
        string idempotencyKey,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        // Same scoped DbContext may already track a successful prior claim;
        // Add() would throw an identity conflict before the unique-index catch.
        if (_db.ProcessedMessages.Local.Any(x =>
                x.ConsumerName == consumerName && x.IdempotencyKey == idempotencyKey))
        {
            return false;
        }

        var exists = await _db.ProcessedMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.ConsumerName == consumerName && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (exists)
        {
            return false;
        }

        var entity = new ProcessedMessage(
            consumerName,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            messageType);

        _db.ProcessedMessages.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var entry = _db.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
