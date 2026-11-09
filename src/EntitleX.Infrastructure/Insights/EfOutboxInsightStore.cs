using EntitleX.Application.Abstractions;
using EntitleX.Application.Insights.Models;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using EntitleX.SharedKernel.Ids;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Insights;

public sealed class EfOutboxInsightStore(EntitleXDbContext db) : IOutboxInsightStore
{
    public async Task<IReadOnlyList<OutboxPoisonInsight>> ListPoisonByRequestAsync(
        RequestId requestId,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 100);
        return await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.RequestId == requestId.Value && m.Status == OutboxMessageStatus.Poison)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(limit)
            .Select(m => new OutboxPoisonInsight(
                m.Id,
                m.MessageType,
                m.Destination,
                m.LastError,
                m.AttemptCount,
                m.CreatedAtUtc,
                m.LogicalOperationId))
            .ToListAsync(cancellationToken);
    }
}
