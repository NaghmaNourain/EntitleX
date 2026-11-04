using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Saga;

namespace EntitleX.Domain.UnitTests;

public sealed class ApprovalReminderPolicyTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void IsReminderDue_WhenPendingBeyondThreshold_AndNoPriorReminder()
    {
        var due = ApprovalReminderPolicy.IsReminderDue(
            pendingSinceUtc: T0,
            nowUtc: T0.AddDays(7),
            pendingThreshold: TimeSpan.FromDays(7),
            reminderAlreadySentAtUtc: null);

        Assert.True(due);
    }

    [Fact]
    public void IsReminderDue_IsIdempotent_WhenReminderAlreadyRecorded()
    {
        var due = ApprovalReminderPolicy.IsReminderDue(
            pendingSinceUtc: T0,
            nowUtc: T0.AddDays(30),
            pendingThreshold: TimeSpan.FromDays(7),
            reminderAlreadySentAtUtc: T0.AddDays(7));

        Assert.False(due);
    }

    [Fact]
    public void IsEscalationDue_AfterConfiguredDelay_OnlyOnce()
    {
        var reminderAt = T0.AddDays(7);
        Assert.True(ApprovalReminderPolicy.IsEscalationDue(
            reminderAt,
            reminderAt.AddHours(24),
            TimeSpan.FromHours(24),
            alreadyEscalatedAtUtc: null));

        Assert.False(ApprovalReminderPolicy.IsEscalationDue(
            reminderAt,
            reminderAt.AddHours(48),
            TimeSpan.FromHours(24),
            alreadyEscalatedAtUtc: reminderAt.AddHours(24)));
    }

    [Fact]
    public void GetCurrentStagePendingSince_UsesPriorDecisionForLaterStages()
    {
        var requestId = RequestId.New();
        var approval = ApprovalCase.Open(requestId, maxExtensionDays: 120, ApprovalBand.RenewalManagerThenDealDesk, T0);
        approval.RecordDecision(
            ApprovalDecisionOutcome.Approved,
            "user-rm",
            ["RenewalManager"],
            "RM",
            comment: null,
            correlationId: "c1",
            decidedAtUtc: T0.AddDays(2));

        var pendingSince = ApprovalReminderPolicy.GetCurrentStagePendingSinceUtc(approval);
        Assert.Equal(T0.AddDays(2), pendingSince);
    }

    [Fact]
    public void ApplyConfiguredTerminalState_IsIdempotent_AndUsesConfiguredName()
    {
        var saga = LicenseExtensionSaga.Start(
            RequestId.New(),
            ApprovalBand.RenewalManagerOnly,
            createdAtUtc: T0);

        saga.ApplyConfiguredTerminalState(
            SagaState.Escalated,
            "configured terminal",
            actor: "approval-reminder",
            atUtc: T0.AddDays(8));

        Assert.Equal(SagaState.Escalated, saga.State);

        // Second apply is a no-op (idempotent).
        saga.ApplyConfiguredTerminalState(
            SagaState.Escalated,
            "configured terminal again",
            actor: "approval-reminder",
            atUtc: T0.AddDays(9));

        Assert.Equal(SagaState.Escalated, saga.State);
        Assert.Equal(SagaTimeoutKind.None, saga.TimeoutKind);
    }
}
