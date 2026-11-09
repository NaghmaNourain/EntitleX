using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Outbox;

public sealed class EfOutboxPoisonReplayStore(EntitleXDbContext db) : IOutboxPoisonReplayStore
{
    public async Task<IReadOnlyList<Guid>> ReplayPoisonAsync(
        Guid? outboxMessageId,
        int take,
        CancellationToken cancellationToken = default)
    {
        List<OutboxMessage> rows;
        if (outboxMessageId is { } id)
        {
            var one = await db.OutboxMessages
                .SingleOrDefaultAsync(
                    m => m.Id == id && m.Status == OutboxMessageStatus.Poison,
                    cancellationToken);
            rows = one is null ? [] : [one];
        }
        else
        {
            rows = await db.OutboxMessages
                .Where(m => m.Status == OutboxMessageStatus.Poison)
                .OrderBy(m => m.CreatedAtUtc)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.ReplayFromPoison(now);
        }

        return rows.Select(r => r.Id).ToList();
    }
}
