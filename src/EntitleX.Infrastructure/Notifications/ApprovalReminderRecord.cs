using EntitleX.BuildingBlocks.Ids;

namespace EntitleX.Infrastructure.Notifications;

/// <summary>Idempotent record of an approval reminder (and optional later escalation).</summary>
public sealed class ApprovalReminderRecord
{
    private ApprovalReminderRecord()
    {
        WindowKey = null!;
    }

    public ApprovalReminderRecord(
        Guid id,
        Guid approvalCaseId,
        RequestId requestId,
        int stageOrder,
        string windowKey,
        DateTimeOffset reminderSentAtUtc,
        DateTimeOffset? escalatedAtUtc = null,
        string? terminalStateApplied = null)
    {
        Id = id;
        ApprovalCaseId = approvalCaseId;
        RequestId = requestId;
        StageOrder = stageOrder;
        WindowKey = windowKey;
        ReminderSentAtUtc = reminderSentAtUtc;
        EscalatedAtUtc = escalatedAtUtc;
        TerminalStateApplied = terminalStateApplied;
    }

    public Guid Id { get; private set; }

    public Guid ApprovalCaseId { get; private set; }

    public RequestId RequestId { get; private set; }

    public int StageOrder { get; private set; }

    public string WindowKey { get; private set; }

    public DateTimeOffset ReminderSentAtUtc { get; private set; }

    public DateTimeOffset? EscalatedAtUtc { get; private set; }

    public string? TerminalStateApplied { get; private set; }

    public void MarkEscalated(string terminalStateApplied, DateTimeOffset escalatedAtUtc)
    {
        if (EscalatedAtUtc is not null)
        {
            return;
        }

        EscalatedAtUtc = escalatedAtUtc;
        TerminalStateApplied = terminalStateApplied;
    }
}
