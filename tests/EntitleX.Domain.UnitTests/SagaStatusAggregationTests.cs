using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Domain.UnitTests;

public sealed class SagaStatusAggregationTests
{
    [Fact]
    public void Aggregate_AllSucceeded_Completed()
    {
        var (saga, op1, op2) = CreateProcessingSaga(itemCount: 2);

        saga.RecordItemSucceeded(op1);
        Assert.Equal(SagaState.Processing, saga.State);

        saga.RecordItemSucceeded(op2);
        Assert.Equal(SagaState.Completed, saga.State);
        Assert.Equal(LicenseExtensionRequestStatus.Fulfilled, SagaRequestStatusMap.ToRequestStatus(saga.State));
        Assert.Equal(2, saga.SucceededItemCount);
        Assert.Equal(0, saga.FailedItemCount);
    }

    [Fact]
    public void Aggregate_AllFailed_Failed()
    {
        var (saga, op1, op2) = CreateProcessingSaga(itemCount: 2);

        saga.RecordItemFailed(op1, "E1", "boom");
        saga.RecordItemFailed(op2, "E2", "boom");

        Assert.Equal(SagaState.Failed, saga.State);
        Assert.Equal(LicenseExtensionRequestStatus.Failed, SagaRequestStatusMap.ToRequestStatus(saga.State));
    }

    [Fact]
    public void Aggregate_PartialFailure_PartiallyCompleted_ForwardRecovery()
    {
        var (saga, op1, op2) = CreateProcessingSaga(itemCount: 2);

        saga.RecordItemSucceeded(op1);
        saga.RecordItemFailed(op2, "PROVIDER_ERROR", "vendor rejected");

        Assert.Equal(SagaState.PartiallyCompleted, saga.State);
        Assert.Equal(
            LicenseExtensionRequestStatus.PartiallyFulfilled,
            SagaRequestStatusMap.ToRequestStatus(saga.State));
        Assert.Equal(1, saga.SucceededItemCount);
        Assert.Equal(1, saga.FailedItemCount);

        // Forward recovery: successful item cannot be compensated.
        Assert.Throws<DomainException>(() =>
            saga.RecordItemFailed(op1, "NOPE", "cannot compensate"));

        Assert.Equal(
            SagaItemOutcomeStatus.Succeeded,
            saga.ItemOutcomes.Single(i => i.LogicalOperationId.Equals(op1)).Status);
    }

    [Fact]
    public void Aggregate_DoesNotCompleteWhilePending()
    {
        var (saga, op1, _) = CreateProcessingSaga(itemCount: 2);

        saga.RecordItemSucceeded(op1);
        Assert.Equal(SagaState.Processing, saga.State);
        Assert.False(saga.AllItemsTerminal);
    }

    [Fact]
    public void AggregateStatus_MixedCounts_DocumentsForwardRecoveryReason()
    {
        var (saga, op1, op2) = CreateProcessingSaga(itemCount: 2);
        saga.RecordItemSucceeded(op1);
        saga.RecordItemFailed(op2, "X", "y");

        var last = saga.Transitions[^1];
        Assert.Equal(SagaState.PartiallyCompleted, last.ToState);
        Assert.Contains("forward recovery", last.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotFailSucceededItem_LeavesSiblingProcessing()
    {
        var (saga, op1, op2) = CreateProcessingSaga(itemCount: 2);
        saga.RecordItemSucceeded(op1);

        Assert.Throws<DomainException>(() =>
            saga.RecordItemFailed(op1, "NOPE", "cannot compensate"));

        Assert.Equal(SagaState.Processing, saga.State);

        saga.RecordItemFailed(op2, "E", "fail");
        Assert.Equal(SagaState.PartiallyCompleted, saga.State);
    }

    [Fact]
    public void CorrectItemSucceeded_FromTerminalFailed_ReopensToCompleted()
    {
        var (saga, op1, _) = CreateProcessingSaga(itemCount: 1);
        saga.RecordItemFailed(op1, "E", "false failed");
        Assert.Equal(SagaState.Failed, saga.State);

        saga.CorrectItemSucceededFromReconciliation(op1);

        Assert.Equal(SagaState.Completed, saga.State);
        Assert.Equal(
            SagaItemOutcomeStatus.Succeeded,
            saga.ItemOutcomes.Single().Status);
    }

    [Fact]
    public void CorrectItemSucceeded_FromTerminalFailed_Mixed_ReopensToPartiallyCompleted()
    {
        var (saga, op1, op2) = CreateProcessingSaga(itemCount: 2);
        saga.RecordItemFailed(op1, "E1", "a");
        saga.RecordItemFailed(op2, "E2", "b");
        Assert.Equal(SagaState.Failed, saga.State);

        saga.CorrectItemSucceededFromReconciliation(op1);

        Assert.Equal(SagaState.PartiallyCompleted, saga.State);
        Assert.Equal(1, saga.SucceededItemCount);
        Assert.Equal(1, saga.FailedItemCount);
    }

    private static (LicenseExtensionSaga Saga, LogicalOperationId Op1, LogicalOperationId Op2)
        CreateProcessingSaga(int itemCount)
    {
        var saga = LicenseExtensionSaga.Start(RequestId.New(), ApprovalBand.AutoApprove);
        var items = Enumerable.Range(0, itemCount)
            .Select(_ => (
                RequestItemId.New(),
                EntitlementId.From($"ent-{Guid.NewGuid():N}"),
                LogicalOperationId.New()))
            .ToList();

        saga.QueueFulfillment(items);
        saga.MarkProcessing();

        return (saga, items[0].Item3, items.Count > 1 ? items[1].Item3 : items[0].Item3);
    }
}
