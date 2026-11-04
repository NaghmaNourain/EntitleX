using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.UnitTests;

public sealed class ApprovalRulesTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Theory]
    [InlineData(1, ApprovalBand.AutoApprove)]
    [InlineData(30, ApprovalBand.AutoApprove)]
    [InlineData(31, ApprovalBand.RenewalManagerOnly)]
    [InlineData(90, ApprovalBand.RenewalManagerOnly)]
    [InlineData(91, ApprovalBand.RenewalManagerThenDealDesk)]
    [InlineData(365, ApprovalBand.RenewalManagerThenDealDesk)]
    [InlineData(366, ApprovalBand.Unsupported)]
    [InlineData(500, ApprovalBand.Unsupported)]
    public void EvaluateBand_MapsDayBands(int days, ApprovalBand expected)
    {
        Assert.Equal(expected, ApprovalRules.EvaluateBand(days));
    }

    [Fact]
    public void EvaluateBand_WhenLessThanOneDay_Throws()
    {
        Assert.Throws<DomainException>(() => ApprovalRules.EvaluateBand(0));
    }

    [Fact]
    public void RequiredStages_MatchBand()
    {
        Assert.Empty(ApprovalRules.RequiredStages(ApprovalBand.AutoApprove));
        Assert.Equal(
            [ApprovalStageRole.RenewalManager],
            ApprovalRules.RequiredStages(ApprovalBand.RenewalManagerOnly));
        Assert.Equal(
            [ApprovalStageRole.RenewalManager, ApprovalStageRole.DealDeskManager],
            ApprovalRules.RequiredStages(ApprovalBand.RenewalManagerThenDealDesk));
        Assert.Empty(ApprovalRules.RequiredStages(ApprovalBand.Unsupported));
    }

    [Fact]
    public void ComputeMaxExtensionDays_UsesLongestItem()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-DAYS",
            "rep@entitlex.local",
            [
                Item(Baseline.AddDays(10)),
                Item(Baseline.AddDays(45))
            ]);

        Assert.Equal(45, ApprovalRules.ComputeMaxExtensionDays(request.Items));
        Assert.Equal(ApprovalBand.RenewalManagerOnly, ApprovalRules.EvaluateBand(request.Items));
    }

    private static (
        EntitlementId EntitlementId,
        ProviderSystem Provider,
        DateTimeOffset CurrentExpiryUtc,
        DateTimeOffset RequestedExpiryUtc) Item(DateTimeOffset requested) =>
        (
            EntitlementId.From(Guid.NewGuid().ToString("N")),
            ProviderSystem.CloudShield,
            Baseline,
            requested);
}
