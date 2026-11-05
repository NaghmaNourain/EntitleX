namespace EntitleX.Application.Notifications;

/// <summary>Configuration for the approval-reminder scheduled job / Lambda.</summary>
public sealed class ApprovalReminderOptions
{
    public const string SectionName = "ApprovalReminder";

    /// <summary>How long an approval stage may stay pending before a reminder is sent.</summary>
    public int PendingThresholdDays { get; set; } = 7;

    /// <summary>Delay after a recorded reminder before applying the configured terminal state.</summary>
    public int EscalationDelayHours { get; set; } = 24;

    /// <summary>
    /// Saga state applied after the escalation delay (default <c>Escalated</c>).
    /// Kept configurable so the Lambda does not hardcode a historical name.
    /// </summary>
    public string TerminalSagaState { get; set; } = "Escalated";

    public int BatchSize { get; set; } = 100;

    public TimeSpan PendingThreshold => TimeSpan.FromDays(Math.Max(1, PendingThresholdDays));

    public TimeSpan EscalationDelay => TimeSpan.FromHours(Math.Max(1, EscalationDelayHours));
}
