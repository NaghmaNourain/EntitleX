using EntitleX.BuildingBlocks.Ids;

namespace EntitleX.Application.Abstractions;

public sealed record AuditEventRecord(
    Guid Id,
    string EventType,
    RequestId? RequestId,
    LogicalOperationId? LogicalOperationId,
    string Actor,
    string Detail,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc);

public interface IAuditEventStore
{
    Task AppendAsync(AuditEventRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEventRecord>> ListByRequestAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);
}

public static class AuditEventTypes
{
    public const string ReconciliationPassStarted = "Reconciliation.PassStarted";
    public const string ReconciliationManualTriggered = "Reconciliation.ManualTriggered";
    public const string ReconciliationItemRepairedSuccess = "Reconciliation.ItemRepairedSuccess";
    public const string ReconciliationItemMarkedFailed = "Reconciliation.ItemMarkedFailed";
    public const string ReconciliationItemStillUnknown = "Reconciliation.ItemStillUnknown";
    public const string ReconciliationMismatchDetected = "Reconciliation.MismatchDetected";
    public const string ReconciliationBatchCompleted = "Reconciliation.BatchCompleted";

    public const string ApprovalReminderSent = "Approval.ReminderSent";
    public const string ApprovalReminderEscalated = "Approval.ReminderEscalated";
    public const string ExpirationNotificationSent = "Expiration.NotificationSent";
}
