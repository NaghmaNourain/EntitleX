namespace EntitleX.Application.Notifications;

/// <summary>
/// When provider probes keep returning NotFound/Unknown, age Uncertain items to Failed
/// so selective retry can proceed (M5).
/// </summary>
public sealed class ReconciliationAgingOptions
{
    public const string SectionName = "ReconciliationAging";

    /// <summary>0 disables aging. Default 7 days.</summary>
    public int UncertainMaxAgeDays { get; set; } = 7;
}
