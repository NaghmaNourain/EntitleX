using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.Abstractions;

public sealed record ApprovalReminderRecordDto(
    Guid Id,
    Guid ApprovalCaseId,
    RequestId RequestId,
    int StageOrder,
    string WindowKey,
    DateTimeOffset ReminderSentAtUtc,
    DateTimeOffset? EscalatedAtUtc,
    string? TerminalStateApplied);

public interface IApprovalReminderStore
{
    Task<ApprovalReminderRecordDto?> GetByWindowKeyAsync(
        string windowKey,
        CancellationToken cancellationToken = default);

    Task<ApprovalReminderRecordDto?> GetForStageAsync(
        Guid approvalCaseId,
        int stageOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a reminder record. Returns false when the unique window key already exists
    /// (idempotent concurrent runners).
    /// </summary>
    Task<bool> TryRecordReminderAsync(
        ApprovalReminderRecordDto record,
        CancellationToken cancellationToken = default);

    Task MarkEscalatedAsync(
        string windowKey,
        string terminalStateApplied,
        DateTimeOffset escalatedAtUtc,
        CancellationToken cancellationToken = default);
}
