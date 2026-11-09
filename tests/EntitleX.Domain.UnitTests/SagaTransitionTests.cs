using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Domain.UnitTests;

public sealed class SagaTransitionTests
{
    [Fact]
    public void Start_AutoApprove_GoesToApproved_WithHistory()
    {
        var saga = LicenseExtensionSaga.Start(RequestId.New(), ApprovalBand.AutoApprove);

        Assert.Equal(SagaState.Approved, saga.State);
        Assert.Contains(saga.Transitions, t =>
            t.FromState == SagaState.Requested && t.ToState == SagaState.Approved);
        Assert.Equal(
            LicenseExtensionRequestStatus.Approved,
            SagaRequestStatusMap.ToRequestStatus(saga.State));
    }

    [Fact]
    public void Start_RmThenDealDesk_EnforcesOrdering()
    {
        var saga = LicenseExtensionSaga.Start(
            RequestId.New(),
            ApprovalBand.RenewalManagerThenDealDesk);

        Assert.Equal(SagaState.PendingRenewalManagerApproval, saga.State);
        Assert.True(saga.RequiresDealDesk);

        Assert.Throws<DomainException>(() =>
            saga.RecordDealDeskApproval("dd", "corr"));

        saga.RecordRenewalManagerApproval("rm", "corr-1");
        Assert.Equal(SagaState.PendingDealDeskApproval, saga.State);
        Assert.True(saga.RenewalManagerApproved);

        saga.RecordDealDeskApproval("dd", "corr-2");
        Assert.Equal(SagaState.Approved, saga.State);
        Assert.True(saga.DealDeskApproved);
        Assert.Equal(3, saga.Transitions.Count);
    }

    [Fact]
    public void RecordRenewalManagerApproval_FinalWhenNoDealDesk()
    {
        var saga = LicenseExtensionSaga.Start(
            RequestId.New(),
            ApprovalBand.RenewalManagerOnly);

        saga.RecordRenewalManagerApproval("rm");
        Assert.Equal(SagaState.Approved, saga.State);
        Assert.Equal(SagaTimeoutKind.None, saga.TimeoutKind);
    }

    [Fact]
    public void QueueAndProcess_MapsToInFulfillmentProjection()
    {
        var saga = LicenseExtensionSaga.Start(RequestId.New(), ApprovalBand.AutoApprove);
        var item = SampleItem();

        saga.QueueFulfillment([item]);
        Assert.Equal(SagaState.QueuedForFulfillment, saga.State);
        Assert.True(SagaRequestStatusMap.IsInFulfillmentProjection(saga.State));
        Assert.Equal(
            LicenseExtensionRequestStatus.InFulfillment,
            SagaRequestStatusMap.ToRequestStatus(saga.State));

        saga.MarkProcessing();
        Assert.Equal(SagaState.Processing, saga.State);
        Assert.True(SagaRequestStatusMap.IsInFulfillmentProjection(saga.State));
        Assert.Equal(
            LicenseExtensionRequestStatus.InFulfillment,
            SagaRequestStatusMap.ToRequestStatus(saga.State));
    }

    [Fact]
    public void InvalidTransition_Throws()
    {
        var saga = LicenseExtensionSaga.Start(RequestId.New(), ApprovalBand.AutoApprove);

        Assert.Throws<DomainException>(() =>
            saga.RecordRenewalManagerApproval("rm"));
    }

    [Fact]
    public void Rejection_FromPending_IsTerminal()
    {
        var saga = LicenseExtensionSaga.Start(
            RequestId.New(),
            ApprovalBand.RenewalManagerOnly);

        saga.RecordRejection("rm", reason: "nope");
        Assert.Equal(SagaState.Rejected, saga.State);
        Assert.Throws<DomainException>(() => saga.RecordRenewalManagerApproval("rm"));
    }

    [Fact]
    public void ApprovalTimeout_Escalates()
    {
        var now = DateTimeOffset.Parse("2026-11-04T00:00:00Z");
        var saga = LicenseExtensionSaga.Start(
            RequestId.New(),
            ApprovalBand.RenewalManagerOnly,
            createdAtUtc: now);

        Assert.Equal(SagaTimeoutKind.ApprovalPending, saga.TimeoutKind);
        saga.ApplyTimeout(now.AddDays(4));
        Assert.Equal(SagaState.Escalated, saga.State);
        Assert.Equal(SagaTimeoutKind.None, saga.TimeoutKind);
    }

    [Fact]
    public void FulfillmentTimeout_MarksReconciliationRequired()
    {
        var now = DateTimeOffset.Parse("2026-11-04T00:00:00Z");
        var saga = LicenseExtensionSaga.Start(
            RequestId.New(),
            ApprovalBand.AutoApprove,
            createdAtUtc: now);

        saga.QueueFulfillment([SampleItem()], atUtc: now);
        saga.MarkProcessing(atUtc: now);
        saga.ApplyTimeout(now.AddHours(7));

        Assert.Equal(SagaState.ReconciliationRequired, saga.State);
        Assert.Contains(saga.ItemOutcomes, i => i.Status == SagaItemOutcomeStatus.Uncertain);
    }

    [Fact]
    public void Unsupported_StartsRejected()
    {
        var saga = LicenseExtensionSaga.Start(RequestId.New(), ApprovalBand.Unsupported);
        Assert.Equal(SagaState.Rejected, saga.State);
    }

    private static (RequestItemId, EntitlementId, LogicalOperationId) SampleItem() =>
        (RequestItemId.New(), EntitlementId.From("ent-1"), LogicalOperationId.New());
}
