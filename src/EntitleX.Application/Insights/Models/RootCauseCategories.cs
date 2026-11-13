namespace EntitleX.Application.Insights.Models;

/// <summary>
/// Fixed RootCauseCategory values derived from real saga / reconciliation failure modes.
/// See docs/failure-scenarios.md and ProviderOperationLookupKind.
/// </summary>
public static class RootCauseCategories
{
    public const string ProviderTimeout = "ProviderTimeout";
    public const string ProviderStatusUnknown = "ProviderStatusUnknown";
    public const string ProviderNotFound = "ProviderNotFound";
    public const string ProviderRejected = "ProviderRejected";
    public const string DuplicateApply = "DuplicateApply";
    public const string MessageRedelivery = "MessageRedelivery";
    public const string ReconciliationMismatch = "ReconciliationMismatch";
    public const string PoisonOutbox = "PoisonOutbox";
    public const string ApprovalTimeout = "ApprovalTimeout";
    public const string ConcurrentRetryConflict = "ConcurrentRetryConflict";
    public const string Unknown = "Unknown";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ProviderTimeout,
        ProviderStatusUnknown,
        ProviderNotFound,
        ProviderRejected,
        DuplicateApply,
        MessageRedelivery,
        ReconciliationMismatch,
        PoisonOutbox,
        ApprovalTimeout,
        ConcurrentRetryConflict,
        Unknown
    };

    public static string JoinForPrompt() => string.Join(", ", All.OrderBy(x => x, StringComparer.Ordinal));
}
