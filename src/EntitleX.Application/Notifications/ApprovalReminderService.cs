using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.Notifications;

public interface IApprovalReminderService
{
    Task<ApprovalReminderRunResult> RunAsync(
        DateTimeOffset? nowUtc = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

public sealed record ApprovalReminderRunResult(
    int CandidatesScanned,
    int RemindersSent,
    int EscalationsApplied,
    int SkippedIdempotent);

public sealed class ApprovalReminderService : IApprovalReminderService
{
    private readonly IApprovalCaseRepository _approvals;
    private readonly ILicenseExtensionRequestRepository _requests;
    private readonly ILicenseExtensionSagaRepository _sagas;
    private readonly IApprovalReminderStore _reminderStore;
    private readonly IApprovalNotificationService _notifications;
    private readonly IAuditEventStore _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApprovalReminderOptions _options;
    private readonly ILogger<ApprovalReminderService> _logger;

    public ApprovalReminderService(
        IApprovalCaseRepository approvals,
        ILicenseExtensionRequestRepository requests,
        ILicenseExtensionSagaRepository sagas,
        IApprovalReminderStore reminderStore,
        IApprovalNotificationService notifications,
        IAuditEventStore audit,
        IUnitOfWork unitOfWork,
        IOptions<ApprovalReminderOptions> options,
        ILogger<ApprovalReminderService> logger)
    {
        _approvals = approvals;
        _requests = requests;
        _sagas = sagas;
        _reminderStore = reminderStore;
        _notifications = notifications;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ApprovalReminderRunResult> RunAsync(
        DateTimeOffset? nowUtc = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var correlation = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();

        var terminal = ResolveTerminalState();
        var pending = await _approvals.ListPendingAsync(_options.BatchSize, cancellationToken);

        var remindersSent = 0;
        var escalations = 0;
        var skipped = 0;

        foreach (var approvalCase in pending)
        {
            var stage = approvalCase.CurrentStage;
            if (stage is null)
            {
                skipped++;
                continue;
            }

            var pendingSince = ApprovalReminderPolicy.GetCurrentStagePendingSinceUtc(approvalCase);
            var windowKey = NotificationWindowKeys.ForApprovalReminder(approvalCase.Id, stage.Order);
            var existing = await _reminderStore.GetByWindowKeyAsync(windowKey, cancellationToken);

            if (ApprovalReminderPolicy.IsReminderDue(
                    pendingSince,
                    now,
                    _options.PendingThreshold,
                    existing?.ReminderSentAtUtc))
            {
                var request = await _requests.GetByIdAsync(approvalCase.RequestId, cancellationToken);
                if (request is null)
                {
                    _logger.LogWarning(
                        "Skipping reminder for approval {ApprovalCaseId}: request {RequestId} missing",
                        approvalCase.Id,
                        approvalCase.RequestId.Value);
                    skipped++;
                    continue;
                }

                var record = new ApprovalReminderRecordDto(
                    Guid.NewGuid(),
                    approvalCase.Id,
                    approvalCase.RequestId,
                    stage.Order,
                    windowKey,
                    now,
                    EscalatedAtUtc: null,
                    TerminalStateApplied: null);

                if (!await _reminderStore.TryRecordReminderAsync(record, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                await _notifications.NotifyApprovalReminderAsync(
                    request,
                    approvalCase,
                    stage,
                    now - pendingSince,
                    cancellationToken);

                await _audit.AppendAsync(
                    new AuditEventRecord(
                        Guid.NewGuid(),
                        AuditEventTypes.ApprovalReminderSent,
                        approvalCase.RequestId,
                        LogicalOperationId: null,
                        Actor: "approval-reminder",
                        Detail: $"Reminder sent for stage {stage.Order} after {_options.PendingThresholdDays}d threshold (window={windowKey})",
                        correlation,
                        now),
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                remindersSent++;
                existing = record;
            }

            if (ApprovalReminderPolicy.IsEscalationDue(
                    existing?.ReminderSentAtUtc,
                    now,
                    _options.EscalationDelay,
                    existing?.EscalatedAtUtc))
            {
                var saga = await _sagas.GetByRequestIdAsync(approvalCase.RequestId, cancellationToken);
                if (saga is null)
                {
                    skipped++;
                    continue;
                }

                // Re-check pending — may have been decided since reminder.
                var refreshed = await _approvals.GetByIdAsync(approvalCase.Id, cancellationToken);
                if (refreshed is null || refreshed.Status != ApprovalCaseStatus.Pending)
                {
                    skipped++;
                    continue;
                }

                saga.ApplyConfiguredTerminalState(
                    terminal,
                    $"Approval reminder escalation after {_options.EscalationDelayHours}h (configured terminal={terminal})",
                    actor: "approval-reminder",
                    correlationId: correlation,
                    atUtc: now);

                await _reminderStore.MarkEscalatedAsync(
                    windowKey,
                    terminal.ToString(),
                    now,
                    cancellationToken);

                await _audit.AppendAsync(
                    new AuditEventRecord(
                        Guid.NewGuid(),
                        AuditEventTypes.ApprovalReminderEscalated,
                        approvalCase.RequestId,
                        LogicalOperationId: null,
                        Actor: "approval-reminder",
                        Detail: $"Applied configured terminal state '{terminal}' (window={windowKey})",
                        correlation,
                        now),
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                escalations++;
            }
            else if (existing is not null && existing.EscalatedAtUtc is not null)
            {
                skipped++;
            }
        }

        _logger.LogInformation(
            "Approval reminder run scanned={Scanned} reminders={Reminders} escalations={Escalations} skipped={Skipped} correlation={Correlation}",
            pending.Count,
            remindersSent,
            escalations,
            skipped,
            correlation);

        return new ApprovalReminderRunResult(pending.Count, remindersSent, escalations, skipped);
    }

    private SagaState ResolveTerminalState()
    {
        if (!Enum.TryParse<SagaState>(_options.TerminalSagaState, ignoreCase: true, out var state))
        {
            throw new InvalidOperationException(
                $"ApprovalReminder:TerminalSagaState '{_options.TerminalSagaState}' is not a valid SagaState.");
        }

        return state;
    }
}
