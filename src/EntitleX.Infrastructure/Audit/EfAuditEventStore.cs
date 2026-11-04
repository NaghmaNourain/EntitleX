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
            .ToListAsync(cancellationToken);

        return rows.Select(e => new AuditEventRecord(
            e.Id,
            e.EventType,
            e.RequestId,
            e.LogicalOperationId,
            e.Actor,
            e.Detail,
            e.CorrelationId,
            e.OccurredAtUtc)).ToList();
    }
}
