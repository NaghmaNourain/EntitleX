using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Audit;

public sealed class EfAuditEventStore : IAuditEventStore
{
    private readonly EntitleXDbContext _db;

    public EfAuditEventStore(EntitleXDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(AuditEventRecord record, CancellationToken cancellationToken = default)
    {
        _db.AuditEvents.Add(new AuditEvent(
            record.Id,
            record.EventType,
            record.RequestId,
            record.LogicalOperationId,
            record.Actor,
            record.Detail,
            record.CorrelationId,
            record.OccurredAtUtc));

        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AuditEventRecord>> ListByRequestAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.AuditEvents
            .AsNoTracking()
            .Where(e => e.RequestId == requestId)
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<AuditEventRecord>> ListByRequestKeysetAsync(
        RequestId requestId,
        int take,
        DateTimeOffset? afterOccurredAtUtc,
        Guid? afterId,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var query = _db.AuditEvents
            .AsNoTracking()
            .Where(e => e.RequestId == requestId);

        if (afterOccurredAtUtc is not null && afterId is not null)
        {
            var cursorTime = afterOccurredAtUtc.Value;
            var cursorId = afterId.Value;
            query = query.Where(e =>
                e.OccurredAtUtc > cursorTime
                || (e.OccurredAtUtc == cursorTime && e.Id.CompareTo(cursorId) > 0)); // EF translates Guid CompareTo for Npgsql
        }

        var rows = await query
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static AuditEventRecord Map(AuditEvent e) =>
        new(
            e.Id,
            e.EventType,
            e.RequestId,
            e.LogicalOperationId,
            e.Actor,
            e.Detail,
            e.CorrelationId,
            e.OccurredAtUtc);
}
