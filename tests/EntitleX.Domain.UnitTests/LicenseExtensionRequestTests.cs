using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.UnitTests;

public sealed class LicenseExtensionRequestTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
    private static readonly DateTimeOffset Requested = Baseline.AddMonths(6);

    [Fact]
    public void CreateFresh_WithValidItems_SetsDefaultsAndNullParent()
    {
        var request = CreateSampleRequest(itemCount: 2);

        Assert.Null(request.ParentId);
        Assert.Equal(LicenseExtensionRequestStatus.Draft, request.Status);
        Assert.Equal(0, request.ApprovalVersion);
        Assert.Null(request.ApprovedScopeHash);
        Assert.Equal(2, request.Items.Count);
        Assert.All(request.Items, item =>
        {
            Assert.True(item.RequestedExpiryUtc > item.CurrentExpiryUtc);
            Assert.NotEqual(Guid.Empty, item.LogicalOperationId.Value);
            Assert.NotEqual(Guid.Empty, item.AttemptId.Value);
        });
    }

    [Fact]
    public void CreateFresh_WhenParentIdProvided_Throws()
    {
        var exception = Assert.Throws<DomainException>(() =>
            LicenseExtensionRequest.CreateFresh(
                "EXT-1",
                "alice@example.com",
                [SampleItem("ent-1")],
                parentId: RequestId.New()));

        Assert.Contains("null ParentId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateFresh_WhenMoreThanMaxItems_Throws()
    {
        var items = Enumerable.Range(1, LicenseExtensionRequest.MaxItems + 1)
            .Select(i => SampleItem($"ent-{i}"))
            .ToArray();

        var exception = Assert.Throws<DomainException>(() =>
            LicenseExtensionRequest.CreateFresh("EXT-OVERFLOW", "alice@example.com", items));

        Assert.Contains("1000", exception.Message);
    }

    [Fact]
    public void CreateFresh_WhenRequestedExpiryNotAfterBaseline_Throws()
    {
        Assert.Throws<DomainException>(() =>
            LicenseExtensionRequest.CreateFresh(
                "EXT-BAD-EXPIRY",
                "alice@example.com",
                [(
                    EntitlementId.From("ent-1"),
                    ProviderSystem.CloudShield,
                    Baseline,
                    Baseline)]));
    }

    [Fact]
    public void Submit_FromDraft_TransitionsToSubmitted()
    {
        var request = CreateSampleRequest();

        request.Submit();

        Assert.Equal(LicenseExtensionRequestStatus.Submitted, request.Status);
    }

    [Fact]
    public void Submit_Twice_Throws()
    {
        var request = CreateSampleRequest();
        request.Submit();

        Assert.Throws<DomainException>(() => request.Submit());
    }

    [Fact]
    public void RecordApproval_SetsScopeHashAndIncrementsVersion()
    {
        var request = CreateSampleRequest();
        request.Submit();
        request.MarkPendingApproval();

        request.RecordApproval("scope-hash-abc");

        Assert.Equal(LicenseExtensionRequestStatus.Approved, request.Status);
        Assert.Equal(1, request.ApprovalVersion);
        Assert.Equal("scope-hash-abc", request.ApprovedScopeHash);
    }

    [Fact]
    public void Cancel_FromDraft_TransitionsToCancelled()
    {
        var request = CreateSampleRequest();

        request.Cancel();

        Assert.Equal(LicenseExtensionRequestStatus.Cancelled, request.Status);
    }

    private static LicenseExtensionRequest CreateSampleRequest(int itemCount = 1)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => SampleItem($"ent-{i}"))
            .ToArray();

        return LicenseExtensionRequest.CreateFresh("EXT-TEST-001", "alice@example.com", items);
    }

    private static (
        EntitlementId EntitlementId,
        ProviderSystem Provider,
        DateTimeOffset CurrentExpiryUtc,
        DateTimeOffset RequestedExpiryUtc) SampleItem(string entitlementId) =>
        (
            EntitlementId.From(entitlementId),
            ProviderSystem.CloudShield,
            Baseline,
            Requested);
}
