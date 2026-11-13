using EntitleX.Application.Insights.Models;

namespace EntitleX.Application.Insights;

public sealed class IncidentDiagnosisValidator
{
    public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
    {
        public static ValidationResult Success() => new(true, []);

        public static ValidationResult Failure(params string[] errors) => new(false, errors);
    }

    public ValidationResult Validate(
        IncidentDiagnosis? diagnosis,
        IReadOnlySet<string> allowedEvidenceStepIds)
    {
        if (diagnosis is null)
        {
            return ValidationResult.Failure("Diagnosis was null or could not be parsed.");
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(diagnosis.RootCauseCategory)
            || !RootCauseCategories.All.Contains(diagnosis.RootCauseCategory))
        {
            errors.Add(
                $"RootCauseCategory must be one of: {RootCauseCategories.JoinForPrompt()}.");
        }

        if (string.IsNullOrWhiteSpace(diagnosis.RootCause))
        {
            errors.Add("RootCause must be a non-empty string.");
        }

        if (diagnosis.Confidence is < 0 or > 1)
        {
            errors.Add("Confidence must be in the inclusive range [0, 1].");
        }

        if (string.IsNullOrWhiteSpace(diagnosis.RecommendedAction))
        {
            errors.Add("RecommendedAction must be a non-empty string.");
        }

        var evidence = diagnosis.EvidenceStepIds ?? [];
        foreach (var id in evidence)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add("EvidenceStepIds must not contain null or empty values.");
                continue;
            }

            if (!allowedEvidenceStepIds.Contains(id))
            {
                errors.Add(
                    $"EvidenceStepId '{id}' is not present in the provided saga/attempt citation data.");
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors.ToArray());
    }
}
