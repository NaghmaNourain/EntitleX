namespace EntitleX.Application.Insights.Models;

/// <summary>API-facing validated diagnosis (null when AiAssist is disabled).</summary>
public sealed record IncidentDiagnosisDto(
    string RootCauseCategory,
    string RootCause,
    IReadOnlyList<string> EvidenceStepIds,
    double Confidence,
    string RecommendedAction,
    bool RequiresHumanApproval,
    bool Validated);

public static class IncidentDiagnosisMapping
{
    public static IncidentDiagnosisDto ToDto(this IncidentDiagnosis diagnosis, bool validated) =>
        new(
            diagnosis.RootCauseCategory,
            diagnosis.RootCause,
            diagnosis.EvidenceStepIds ?? [],
            diagnosis.Confidence,
            diagnosis.RecommendedAction,
            diagnosis.RequiresHumanApproval,
            validated);

    public static IncidentDiagnosis UnableToDiagnose(string recommendedAction) =>
        new(
            RootCauseCategories.Unknown,
            "Unable to diagnose",
            [],
            0,
            recommendedAction,
            RequiresHumanApproval: true);
}
