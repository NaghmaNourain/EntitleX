using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Claims pending (or stale locked) outbox rows using PostgreSQL
/// <c>FOR UPDATE SKIP LOCKED</c>.
/// </summary>
public sealed class OutboxClaimer
{
    private readonly EntitleXDbContext _db;
    private readonly OutboxOptions _options;

    public OutboxClaimer(EntitleXDbContext db, IOptions<MessagingOptions> options)
    {
        _db = db;
        _options = options.Value.Outbox;
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Clamp(_options.BatchSize, 1, 100);
        var lockSeconds = Math.Clamp(_options.LockDurationSeconds, 5, 300);
        var now = DateTimeOffset.UtcNow;
        var lockedUntil = now.AddSeconds(lockSeconds);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var connection = _db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;
        command.CommandText =
            """
            WITH claimed AS (
                SELECT id
                FROM outbox_messages
                WHERE (status = 'Pending' AND available_at_utc <= @now)
                   OR (status = 'Processing' AND locked_until_utc IS NOT NULL AND locked_until_utc < @now)
                ORDER BY available_at_utc
                LIMIT @batch
                FOR UPDATE SKIP LOCKED
            )
            UPDATE outbox_messages AS o
            SET status = 'Processing',
                attempt_count = o.attempt_count + 1,
                locked_until_utc = @lockedUntil,
                locked_by = @workerId
            FROM claimed
            WHERE o.id = claimed.id
            RETURNING o.id;
            """;

        command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
        command.Parameters.Add(new NpgsqlParameter("lockedUntil", NpgsqlDbType.TimestampTz) { Value = lockedUntil });
        command.Parameters.Add(new NpgsqlParameter("workerId", NpgsqlDbType.Varchar) { Value = workerId });
        command.Parameters.Add(new NpgsqlParameter("batch", NpgsqlDbType.Integer) { Value = batchSize });

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return [];
        }

        // Detach any tracked stubs so we reload claimed rows with Processing state.
        _db.ChangeTracker.Clear();

        return await _db.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .OrderBy(m => m.AvailableAtUtc)
            .ToListAsync(cancellationToken);
    }
}
