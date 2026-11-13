using EntitleX.Application.Insights;
using EntitleX.Application.Insights.Models;

namespace EntitleX.Application.UnitTests.Insights;

public sealed class IncidentDiagnosisValidatorTests
{
    private readonly IncidentDiagnosisValidator _validator = new();

    [Fact]
    public void Accepts_Valid_Diagnosis()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "step-1", "step-2" };
        var diagnosis = new IncidentDiagnosis(
            RootCauseCategories.MessageRedelivery,
            "Kafka result was redelivered.",
            ["step-1"],
            0.5,
            "None",
            RequiresHumanApproval: false);

        var result = _validator.Validate(diagnosis, allowed);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Rejects_Unknown_Category_OutOfRange_Confidence_And_Hallucinated_Evidence()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "step-1" };
        var diagnosis = new IncidentDiagnosis(
            "MadeUpCategory",
            "x",
            ["missing"],
            1.5,
            "RetryFailedItems",
            RequiresHumanApproval: true);

        var result = _validator.Validate(diagnosis, allowed);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("RootCauseCategory", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("Confidence", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("missing", StringComparison.Ordinal));
    }
}
