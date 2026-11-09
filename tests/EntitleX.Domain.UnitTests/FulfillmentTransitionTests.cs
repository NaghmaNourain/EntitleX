using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.UnitTests;

public sealed class FulfillmentTransitionTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    [Fact]
    public void BeginFulfillment_FromApproved_Transitions()
    {
        var request = CreateApproved();
        request.BeginFulfillment();
        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment, request.Status);
    }

    [Fact]
    public void CompleteFulfillment_AllSucceeded_BecomesFulfilled()
    {
        var request = CreateInFulfillment();
        request.CompleteFulfillment(succeededCount: 2, failedCount: 0);
        Assert.Equal(LicenseExtensionRequestStatus.Fulfilled, request.Status);
    }

    [Fact]
    public void CompleteFulfillment_AllFailed_BecomesFailed()
    {
        var request = CreateInFulfillment();
        request.CompleteFulfillment(succeededCount: 0, failedCount: 2);
        Assert.Equal(LicenseExtensionRequestStatus.Failed, request.Status);
    }

    [Fact]
    public void CompleteFulfillment_Mixed_BecomesPartiallyFulfilled()
    {
        var request = CreateInFulfillment();
        request.CompleteFulfillment(succeededCount: 1, failedCount: 1);
        Assert.Equal(LicenseExtensionRequestStatus.PartiallyFulfilled, request.Status);
    }

    [Fact]
    public void CompleteFulfillment_FromDraft_Throws()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-F-BAD",
            "a@example.com",
            [SampleItem()]);

        Assert.Throws<DomainException>(() => request.CompleteFulfillment(1, 0));
    }

    private static LicenseExtensionRequest CreateApproved()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-F-1",
            "a@example.com",
            [SampleItem(), SampleItem("ent-2")]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        return request;
    }

    private static LicenseExtensionRequest CreateInFulfillment()
    {
        var request = CreateApproved();
        request.BeginFulfillment();
        return request;
    }

    private static (
        EntitlementId EntitlementId,
        ProviderSystem Provider,
        DateTimeOffset CurrentExpiryUtc,
        DateTimeOffset RequestedExpiryUtc) SampleItem(string id = "ent-1") =>
        (
            EntitlementId.From(id),
            ProviderSystem.CloudShield,
            Baseline,
            Baseline.AddDays(30));
}
