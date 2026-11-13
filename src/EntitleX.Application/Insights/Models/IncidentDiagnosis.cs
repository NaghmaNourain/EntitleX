namespace EntitleX.Application.Insights.Models;

/// <summary>
/// Structured RCA diagnosis produced via Microsoft.Extensions.AI structured output.
/// Must be validated before returning to callers.
/// </summary>
public sealed record IncidentDiagnosis(
    string RootCauseCategory,
    string RootCause,
    string[] EvidenceStepIds,
    double Confidence,
    string RecommendedAction,
    bool RequiresHumanApproval);
