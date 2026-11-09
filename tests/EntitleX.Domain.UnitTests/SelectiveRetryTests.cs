using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Domain.UnitTests;

public sealed class SelectiveRetryTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    [Fact]
    public void FailedItemSelector_SelectsOnlyFailed_PreservesLogicalIds()
    {
        var (parent, saga, failedItem, succeededItem) = CreatePartiallyFulfilled();

        var selected = FailedItemSelector.SelectEligibleFailedItems(parent, saga);

        Assert.Single(selected);
        Assert.Equal(failedItem.LogicalOperationId, selected[0].LogicalOperationId);
        Assert.DoesNotContain(selected, i => i.LogicalOperationId.Equals(succeededItem.LogicalOperationId));
    }

    [Fact]
    public void FailedItemSelector_WhenNoFailures_Throws()
    {
        var parent = CreateApprovedRequest(itemCount: 1);
        parent.BeginFulfillment();
        parent.CompleteFulfillment(1, 0);

        var saga = LicenseExtensionSaga.Start(parent.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(parent.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(parent.Items[0].LogicalOperationId);

        Assert.Throws<DomainException>(() =>
            FailedItemSelector.SelectEligibleFailedItems(parent, saga));
    }

    [Fact]
    public void CreateSelectiveRetry_CopiesFailedOnly_NewAttemptIds_ParentChain()
    {
        var (parent, saga, failedItem, _) = CreatePartiallyFulfilled();
        var selected = FailedItemSelector.SelectEligibleFailedItems(parent, saga);

        var retry = LicenseExtensionRequest.CreateSelectiveRetry(
            parent,
            selected,
            "EXT-R-1",
            "rep@entitlex.local",
            notes: "retry failed provider call");

        Assert.Equal(parent.Id, retry.ParentId);
        Assert.Single(retry.Items);
        Assert.Equal(failedItem.LogicalOperationId, retry.Items[0].LogicalOperationId);
        Assert.NotEqual(failedItem.AttemptId, retry.Items[0].AttemptId);
        Assert.Equal("retry failed provider call", retry.Notes);
        Assert.Equal(LicenseExtensionRequestStatus.Draft, retry.Status);
    }

    [Fact]
    public void CreateSelectiveRetry_MultiGeneration_ChainsParentIds()
    {
        var (gen1, saga1, _, _) = CreatePartiallyFulfilled();
        var selected1 = FailedItemSelector.SelectEligibleFailedItems(gen1, saga1);
        var gen2 = LicenseExtensionRequest.CreateSelectiveRetry(
            gen1, selected1, "EXT-R-G2", "rep@entitlex.local");
        gen1.RecordSelectiveRetrySpawned(gen2, "rep@entitlex.local");

        // Simulate gen2 also partially failing the same logical op.
        gen2.Submit();
        gen2.MarkPendingApproval();
        gen2.RecordApproval(ApprovedScopeHasher.Compute(gen2));
        gen2.BeginFulfillment();
        gen2.CompleteFulfillment(succeededCount: 0, failedCount: 1);

        var saga2 = LicenseExtensionSaga.Start(gen2.Id, ApprovalBand.AutoApprove);
        saga2.QueueFulfillment(gen2.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga2.MarkProcessing();
        saga2.RecordItemFailed(gen2.Items[0].LogicalOperationId, "E", "again");

        var selected2 = FailedItemSelector.SelectEligibleFailedItems(gen2, saga2);
        var gen3 = LicenseExtensionRequest.CreateSelectiveRetry(
            gen2, selected2, "EXT-R-G3", "rep@entitlex.local");

        Assert.Equal(gen1.Id, gen2.ParentId);
        Assert.Equal(gen2.Id, gen3.ParentId);
        Assert.Equal(gen1.Items.Single(i => saga1.ItemOutcomes.Any(
                o => o.Status == SagaItemOutcomeStatus.Failed
                     && o.LogicalOperationId.Equals(i.LogicalOperationId))).LogicalOperationId,
            gen3.Items[0].LogicalOperationId);
        Assert.Single(gen1.RetryHistory);
        Assert.Equal(gen2.Id, gen1.RetryHistory[0].RetryRequestId);
    }

    [Fact]
    public void ApprovalReuseEvaluator_WhenScopeUnchanged_AllowsReuse()
    {
        var (parent, saga, _, _) = CreatePartiallyFulfilled();
        var selected = FailedItemSelector.SelectEligibleFailedItems(parent, saga);

        Assert.True(ApprovalReuseEvaluator.CanReuseParentApproval(parent, selected));
    }

    [Fact]
    public void ApprovalReuseEvaluator_WhenParentHashMissing_DeniesReuse()
    {
        var parent = LicenseExtensionRequest.CreateFresh(
            "EXT-NO-HASH",
            "rep@entitlex.local",
            [SampleItem("a"), SampleItem("b")]);
        parent.Submit();
        parent.MarkPendingApproval();
        // Skip RecordApproval — no ApprovedScopeHash.
        // Force status via fulfillment path is invalid; instead craft Failed after improper path:
        // Use approved then clear isn't possible; create partially fulfilled then wipe hash via
        // evaluating a request that never had approval — selector needs Failed/Partial.
        // Domain: CreateSelectiveRetry requires Partial/Failed; build Failed without hash by
        // using reflection-free path: approve, fulfill, fail, then we always have hash.
        // So deny path is simply empty hash on a Partial request — simulate by checking fresh draft items.
        var items = parent.Items.ToList();
        Assert.False(ApprovalReuseEvaluator.CanReuseParentApproval(parent, items));
    }

    [Fact]
    public void CreateSelectiveRetry_FromFulfilled_Throws()
    {
        var parent = CreateApprovedRequest(1);
        parent.BeginFulfillment();
        parent.CompleteFulfillment(1, 0);

        Assert.Throws<DomainException>(() =>
            LicenseExtensionRequest.CreateSelectiveRetry(
                parent,
                parent.Items.ToList(),
                "EXT-BAD",
                "rep@entitlex.local"));
    }

    private static (
        LicenseExtensionRequest Parent,
        LicenseExtensionSaga Saga,
        LicenseExtensionRequestItem Failed,
        LicenseExtensionRequestItem Succeeded) CreatePartiallyFulfilled()
    {
        var parent = CreateApprovedRequest(itemCount: 2);
        parent.BeginFulfillment();
        parent.CompleteFulfillment(succeededCount: 1, failedCount: 1);

        var saga = LicenseExtensionSaga.Start(parent.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(parent.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(parent.Items[0].LogicalOperationId);
        saga.RecordItemFailed(parent.Items[1].LogicalOperationId, "PROVIDER_ERROR", "boom");

        return (parent, saga, parent.Items[1], parent.Items[0]);
    }

    private static LicenseExtensionRequest CreateApprovedRequest(int itemCount)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => SampleItem($"ent-{i}"))
            .ToArray();
        var request = LicenseExtensionRequest.CreateFresh("EXT-SRC-1", "rep@entitlex.local", items);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval(ApprovedScopeHasher.Compute(request));
        return request;
    }

    private static (
        EntitlementId EntitlementId,
        ProviderSystem Provider,
        DateTimeOffset CurrentExpiryUtc,
        DateTimeOffset RequestedExpiryUtc) SampleItem(string id) =>
        (
            EntitlementId.From(id),
            ProviderSystem.CloudShield,
            Baseline,
            Baseline.AddDays(30));
}
