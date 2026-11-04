using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Saga;

namespace EntitleX.Domain.UnitTests;

public sealed class ReconciliationCorrectiveRepairTests
{
    [Fact]
    public void CorrectItemSucceeded_FromUncertain_CompletesSaga()
    {
        var (saga, op1, op2) = CreateTimedOutSaga();

        saga.CorrectItemSucceededFromReconciliation(op1);
        saga.CorrectItemSucceededFromReconciliation(op2);

        Assert.Equal(SagaState.Completed, saga.State);
        Assert.All(saga.ItemOutcomes, o => Assert.Equal(SagaItemOutcomeStatus.Succeeded, o.Status));
    }

    [Fact]
    public void CorrectItemSucceeded_FromFailed_AllowsForwardRepair()
    {
        var (saga, op1, op2) = CreateProcessingSaga();
        saga.RecordItemFailed(op1, "E", "local lag");
        saga.RecordItemSucceeded(op2);

        Assert.Equal(SagaState.PartiallyCompleted, saga.State);

        saga.CorrectItemSucceededFromReconciliation(op1);

        Assert.Equal(SagaState.Completed, saga.State);
        Assert.Equal(
            SagaItemOutcomeStatus.Succeeded,
            saga.ItemOutcomes.Single(o => o.LogicalOperationId.Equals(op1)).Status);
    }

    [Fact]
    public void KeepItemUncertain_DoesNotBlindFail_AndStaysReconciliationRequired()
    {
        var (saga, op1, _) = CreateTimedOutSaga();

        saga.KeepItemUncertainFromReconciliation(op1, "provider 404 — unknown external effect");

        Assert.Equal(SagaState.ReconciliationRequired, saga.State);
        Assert.Equal(
            SagaItemOutcomeStatus.Uncertain,
            saga.ItemOutcomes.Single(o => o.LogicalOperationId.Equals(op1)).Status);
    }

    [Fact]
    public void RecordItemFailed_FromUncertain_MakesRetryEligiblePartial()
    {
        var (saga, op1, op2) = CreateTimedOutSaga();

        saga.CorrectItemSucceededFromReconciliation(op1);
        saga.RecordItemFailed(op2, "PROVIDER_FAILED", "confirmed failure");

        Assert.Equal(SagaState.PartiallyCompleted, saga.State);
        Assert.Equal(
            SagaItemOutcomeStatus.Failed,
            saga.ItemOutcomes.Single(o => o.LogicalOperationId.Equals(op2)).Status);
    }

    [Fact]
    public void CorrectItemSucceeded_IsIdempotent()
    {
        var (saga, op1, op2) = CreateTimedOutSaga();

        saga.CorrectItemSucceededFromReconciliation(op1);
        saga.CorrectItemSucceededFromReconciliation(op1);
        saga.CorrectItemSucceededFromReconciliation(op2);

        Assert.Equal(SagaState.Completed, saga.State);
        Assert.Equal(2, saga.SucceededItemCount);
    }

    private static (LicenseExtensionSaga Saga, LogicalOperationId Op1, LogicalOperationId Op2)
        CreateTimedOutSaga()
    {
        var (saga, op1, op2) = CreateProcessingSaga();
        saga.ApplyTimeout(DateTimeOffset.UtcNow.AddHours(7));
        Assert.Equal(SagaState.ReconciliationRequired, saga.State);
        return (saga, op1, op2);
    }

    private static (LicenseExtensionSaga Saga, LogicalOperationId Op1, LogicalOperationId Op2)
        CreateProcessingSaga()
    {
        var op1 = LogicalOperationId.New();
        var op2 = LogicalOperationId.New();
        var saga = LicenseExtensionSaga.Start(RequestId.New(), ApprovalBand.AutoApprove);
        saga.QueueFulfillment(
        [
            (RequestItemId.New(), EntitlementId.From("ent-1"), op1),
            (RequestItemId.New(), EntitlementId.From("ent-2"), op2)
        ]);
        saga.MarkProcessing();
        return (saga, op1, op2);
    }
}
