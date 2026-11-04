namespace EntitleX.Domain.Approval;

/// <summary>
/// Pure policy for when a pending approval should be reminded / escalated.
/// Thresholds are injected (not hardcoded) so hosts can configure them.
/// </summary>
public static class ApprovalReminderPolicy
{
    /// <summary>
    /// Returns when the current pending stage became active.
    /// </summary>
    public static DateTimeOffset GetCurrentStagePendingSinceUtc(ApprovalCase approvalCase)
    {
        if (approvalCase.Status != ApprovalCaseStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Approval case {approvalCase.Id} is not pending (status={approvalCase.Status}).");
        }

        if (approvalCase.CurrentStageOrder <= 0)
        {
            return approvalCase.CreatedAtUtc;
        }

        var prior = approvalCase.Decisions
            .Where(d => d.StageOrder == approvalCase.CurrentStageOrder - 1)
            .OrderByDescending(d => d.DecidedAtUtc)
            .FirstOrDefault();

        return prior?.DecidedAtUtc ?? approvalCase.CreatedAtUtc;
    }

    public static bool IsReminderDue(
        DateTimeOffset pendingSinceUtc,
        DateTimeOffset nowUtc,
        TimeSpan pendingThreshold,
        DateTimeOffset? reminderAlreadySentAtUtc)
    {
        if (reminderAlreadySentAtUtc is not null)
        {
            return false;
        }

        return nowUtc - pendingSinceUtc >= pendingThreshold;
    }

    public static bool IsEscalationDue(
        DateTimeOffset? reminderSentAtUtc,
        DateTimeOffset nowUtc,
        TimeSpan escalationDelayAfterReminder,
        DateTimeOffset? alreadyEscalatedAtUtc)
    {
        if (alreadyEscalatedAtUtc is not null)
        {
            return false;
        }

        if (reminderSentAtUtc is null)
        {
            return false;
        }

        return nowUtc - reminderSentAtUtc.Value >= escalationDelayAfterReminder;
    }
}
