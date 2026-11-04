using EntitleX.BuildingBlocks.Ids;

namespace EntitleX.Infrastructure.Audit;

public sealed class AuditEvent
{
    private AuditEvent()
    {
        EventType = null!;
        Actor = null!;
        Detail = null!;
    }

    public AuditEvent(
        Guid id,
        string eventType,
        RequestId? requestId,
        LogicalOperationId? logicalOperationId,
        string actor,
        string detail,
        string? correlationId,
        DateTimeOffset occurredAtUtc)
    {
        Id = id;
        EventType = eventType;
        RequestId = requestId;
        LogicalOperationId = logicalOperationId;
        Actor = actor;
        Detail = detail;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }

    public string EventType { get; private set; }

    public RequestId? RequestId { get; private set; }

    public LogicalOperationId? LogicalOperationId { get; private set; }

    public string Actor { get; private set; }

    public string Detail { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
