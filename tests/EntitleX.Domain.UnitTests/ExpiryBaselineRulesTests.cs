using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.UnitTests;

public sealed class ExpiryBaselineRulesTests
{
    [Fact]
    public void EnsureRequestedExpiryAfterBaseline_WhenAfter_DoesNotThrow()
    {
        var baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var requested = baseline.AddDays(30);

        var exception = Record.Exception(() =>
            ExpiryBaselineRules.EnsureRequestedExpiryAfterBaseline(baseline, requested));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureRequestedExpiryAfterBaseline_WhenEqual_Throws()
    {
        var baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var exception = Assert.Throws<DomainException>(() =>
            ExpiryBaselineRules.EnsureRequestedExpiryAfterBaseline(baseline, baseline));

        Assert.Contains("strictly after", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureRequestedExpiryAfterBaseline_WhenBefore_Throws()
    {
        var baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var requested = baseline.AddDays(-1);

        Assert.Throws<DomainException>(() =>
            ExpiryBaselineRules.EnsureRequestedExpiryAfterBaseline(baseline, requested));
    }
}
