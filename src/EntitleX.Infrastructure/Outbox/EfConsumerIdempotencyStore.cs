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
        _db.ProcessedMessages.Add(new ProcessedMessage(
            consumerName,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            messageType));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
