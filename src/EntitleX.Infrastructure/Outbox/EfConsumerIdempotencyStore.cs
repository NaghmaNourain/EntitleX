using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Stages processed_messages rows without flushing. Callers must SaveChanges
/// in the same unit of work as business side effects (claim-before-flush is unsafe).
/// </summary>
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

        _db.ProcessedMessages.Add(new ProcessedMessage(
            consumerName,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            messageType));

        return true;
    }
}
