using EntitleX.Application.Abstractions;
using EntitleX.Application.Insights;
using EntitleX.Application.Insights.Models;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.UnitTests.Insights;

public sealed class DeterministicInsightComposerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01Z");
    private readonly DeterministicInsightComposer _composer = new();

    [Fact]
    public void Uncertain_WithUnknownProbe_HoldUncertain()
    {
        var (request, saga) = CreateUncertain();
        var item = saga.ItemOutcomes.Single();
        var context = BaseContext(request, saga, new InsightItemSnapshot(
            item.RequestItemId.Value,
            item.EntitlementId.Value,
            item.LogicalOperationId.Value,
            "CloudShield",
            item.Status,
            Ledger: null,
            new ProviderOperationStatus(
                ProviderSystem.CloudShield,
                item.LogicalOperationId,
                ProviderOperationLookupKind.Unknown,
                null,
                null,
                null,
                "timeout")));

        var result = _composer.Compose(context, "Rca");

        Assert.Equal(InsightRecommendedAction.HoldUncertain, result.RecommendedAction);
        Assert.Contains("HoldUncertain", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerSucceeded_SagaUncertain_ManualReconcile()
    {
        var (request, saga) = CreateUncertain();
        var item = saga.ItemOutcomes.Single();
        var ledger = new LocalFulfillmentOutcome(
            request.Id.Value,
            item.RequestItemId.Value,
            item.LogicalOperationId.Value,
            Guid.NewGuid(),
            item.EntitlementId.Value,
            "CloudShield",
            Succeeded: true,
            null,
            null,
            DateTimeOffset.UtcNow);
        var context = BaseContext(request, saga, new InsightItemSnapshot(
            item.RequestItemId.Value,
            item.EntitlementId.Value,
            item.LogicalOperationId.Value,
            "CloudShield",
            item.Status,
            ledger,
            Probe: null));

        var result = _composer.Compose(context, "Rca");

        Assert.Equal(InsightRecommendedAction.ManualReconcile, result.RecommendedAction);
        Assert.True(result.Facts.LedgerMismatchCount >= 1);
    }

    [Fact]
    public void FailedOnly_RetryFailedItems()
    {
        var (request, saga) = CreatePartialFailed();
        var items = saga.ItemOutcomes.Select(o => new InsightItemSnapshot(
            o.RequestItemId.Value,
            o.EntitlementId.Value,
            o.LogicalOperationId.Value,
            "CloudShield",
            o.Status,
            Ledger: null,
            Probe: null)).ToList();
        var context = BaseContext(request, saga, items.ToArray());

        var result = _composer.Compose(context, "Explain");

        Assert.Equal(InsightRecommendedAction.RetryFailedItems, result.RecommendedAction);
    }

    [Fact]
    public void PoisonPresent_InvestigatePoisonOutbox()
    {
        var (request, saga) = CreatePartialFailed();
        var items = saga.ItemOutcomes.Select(o => new InsightItemSnapshot(
            o.RequestItemId.Value,
            o.EntitlementId.Value,
            o.LogicalOperationId.Value,
            "CloudShield",
            o.Status,
            null,
            null)).ToList();
        var context = new RequestInsightContext
        {
            RequestId = request.Id.Value,
            Request = request,
            Approval = null,
            Saga = saga,
            Items = items,
            PoisonOutbox =
            [
                new OutboxPoisonInsight(
                    Guid.NewGuid(),
                    "ProviderLicenseExtensionCommand",
                    "entitlex.provider.cloudshield",
                    "publish failed",
                    8,
                    DateTimeOffset.UtcNow,
                    items[0].LogicalOperationId)
            ],
            RecentAudit = [],
            BuilderNotes = []
        };

        var result = _composer.Compose(context, "Rca");

        Assert.Equal(InsightRecommendedAction.InvestigatePoisonOutbox, result.RecommendedAction);
        Assert.Contains(result.Citations, c => c.Kind == "outbox");
    }

    [Fact]
    public void ApprovalPending_InvestigateApproval()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-INS-APPR",
            "rep@entitlex.local",
            [(EntitlementId.From("ent-1"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))]);
        request.Submit();
        request.MarkPendingApproval();
        var approval = ApprovalCase.Open(request.Id, 30, ApprovalBand.RenewalManagerOnly);
        var context = new RequestInsightContext
        {
            RequestId = request.Id.Value,
            Request = request,
            Approval = approval,
            Saga = null,
            Items = [],
            PoisonOutbox = [],
            RecentAudit = [],
            BuilderNotes = []
        };

        var result = _composer.Compose(context, "Explain");

        Assert.Equal(InsightRecommendedAction.InvestigateApproval, result.RecommendedAction);
    }

    private static RequestInsightContext BaseContext(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        params InsightItemSnapshot[] items) =>
        new()
        {
            RequestId = request.Id.Value,
            Request = request,
            Approval = null,
            Saga = saga,
            Items = items,
            PoisonOutbox = [],
            RecentAudit = [],
            BuilderNotes = []
        };

    private static (LicenseExtensionRequest Request, LicenseExtensionSaga Saga) CreateUncertain()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-INS-1",
            "rep@entitlex.local",
            [(EntitlementId.From("ent-1"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.ApplyTimeout(DateTimeOffset.UtcNow.AddHours(7));
        return (request, saga);
    }

    private static (LicenseExtensionRequest Request, LicenseExtensionSaga Saga) CreatePartialFailed()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-INS-2",
            "rep@entitlex.local",
            [
                (EntitlementId.From("ent-1"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30)),
                (EntitlementId.From("ent-2"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))
            ]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();
        request.CompleteFulfillment(1, 1);

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(request.Items[0].LogicalOperationId);
        saga.RecordItemFailed(request.Items[1].LogicalOperationId, "E", "fail");
        return (request, saga);
    }
}
