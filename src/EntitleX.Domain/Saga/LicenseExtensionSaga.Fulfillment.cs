using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Saga;

public sealed partial class LicenseExtensionSaga
{
    /// <summary>
    /// Marks fulfillment command written to the transactional outbox.
    /// Request status projection: <c>InFulfillment</c>.
    /// </summary>
    public void QueueFulfillment(
        IEnumerable<(RequestItemId ItemId, EntitlementId EntitlementId, LogicalOperationId LogicalOperationId)> items,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (SagaState.Approved or SagaState.Escalated))
        {
            throw new DomainException(
                $"Cannot queue fulfillment from saga state '{State}'.");
        }

        if (State == SagaState.Escalated && !IsApprovedPathComplete())
        {
            throw new DomainException(
                "Cannot queue fulfillment from Escalated until approvals are complete.");
        }

        SeedItemOutcomes(items, now);

        TransitionTo(
            SagaState.QueuedForFulfillment,
            "Fulfillment command enqueued (outbox)",
            actor: null,
            correlationId,
            now);
        ScheduleTimeout(SagaTimeoutKind.FulfillmentProcessing, now.AddHours(6), now);
    }

    /// <summary>
    /// Marks provider commands dispatched / in flight.
    /// Request status projection remains <c>InFulfillment</c>.
    /// </summary>
    public void MarkProcessing(string? correlationId = null, DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (
            SagaState.QueuedForFulfillment or
            SagaState.PartiallyCompleted or
            SagaState.ReconciliationRequired or
            SagaState.Escalated))
        {
            throw new DomainException(
                $"Cannot mark processing from saga state '{State}'.");
        }

        TransitionTo(
            SagaState.Processing,
            "Provider fulfillment in progress",
            actor: null,
            correlationId,
            now);
        ScheduleTimeout(SagaTimeoutKind.FulfillmentProcessing, now.AddHours(6), now);
    }

    public void RecordItemSucceeded(
        LogicalOperationId logicalOperationId,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;
        var item = FindItem(logicalOperationId);
        item.MarkSucceeded(now);
        UpdatedAtUtc = now;
        TryAggregateAfterItemUpdate(correlationId, now);
    }

    public void RecordItemFailed(
        LogicalOperationId logicalOperationId,
        string? errorCode,
        string? errorMessage,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;
        var item = FindItem(logicalOperationId);
        item.MarkFailed(errorCode, errorMessage, now);
        UpdatedAtUtc = now;
        TryAggregateAfterItemUpdate(correlationId, now);
    }

    /// <summary>
    /// Phase 8 corrective repair when the provider confirms Applied for a
    /// LogicalOperationId (including when local previously recorded Failed,
    /// even if the saga is already terminal Failed). Never demotes Succeeded.
    /// Idempotent when the item is already Succeeded.
    /// </summary>
    public void CorrectItemSucceededFromReconciliation(
        LogicalOperationId logicalOperationId,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        var now = atUtc ?? DateTimeOffset.UtcNow;
        var item = FindItem(logicalOperationId);
        if (item.Status == SagaItemOutcomeStatus.Succeeded)
        {
            return;
        }

        if (State == SagaState.Rejected)
        {
            throw new DomainException("Cannot repair item outcomes on a Rejected saga.");
        }

        if (State == SagaState.Completed)
        {
            // Terminal success path — nothing to repair into.
            return;
        }

        if (State != SagaState.Failed)
        {
            EnsureNotTerminal();
        }

        item.CorrectToSucceeded(now);
        UpdatedAtUtc = now;

        if (State == SagaState.Failed)
        {
            ReaggregateAfterFailedRepair(correlationId, now);
            return;
        }

        TryAggregateAfterItemUpdate(correlationId, now);
    }

    /// <summary>
    /// Leaves an item Uncertain after an inconclusive provider probe.
    /// Ensures saga remains <see cref="SagaState.ReconciliationRequired"/>.
    /// </summary>
    public void KeepItemUncertainFromReconciliation(
        LogicalOperationId logicalOperationId,
        string reason,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        if (State is SagaState.Completed or SagaState.Failed or SagaState.Rejected)
        {
            return;
        }

        var now = atUtc ?? DateTimeOffset.UtcNow;
        var item = FindItem(logicalOperationId);
        item.KeepUncertain(reason, now);
        UpdatedAtUtc = now;

        if (State != SagaState.ReconciliationRequired)
        {
            if (State is SagaState.QueuedForFulfillment or SagaState.Processing
                or SagaState.PartiallyCompleted or SagaState.Escalated)
            {
                TransitionTo(
                    SagaState.ReconciliationRequired,
                    reason,
                    actor: "reconciliation",
                    correlationId,
                    now);
            }
        }
    }

    /// <summary>
    /// Aggregates item outcomes into a terminal or partial saga state.
    /// Mixed success/failure → <see cref="SagaState.PartiallyCompleted"/>;
    /// successful siblings are never rolled back.
    /// </summary>
    public SagaState AggregateStatus(string? correlationId = null, DateTimeOffset? atUtc = null)
    {
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is SagaState.Completed or SagaState.Failed or SagaState.Rejected)
        {
            return State;
        }

        if (_itemOutcomes.Count == 0)
        {
            throw new DomainException("Cannot aggregate saga status without item outcomes.");
        }

        if (_itemOutcomes.Any(i => i.Status == SagaItemOutcomeStatus.Uncertain))
        {
            if (State != SagaState.ReconciliationRequired)
            {
                TransitionTo(
                    SagaState.ReconciliationRequired,
                    "One or more item outcomes are uncertain",
                    actor: null,
                    correlationId,
                    now);
            }

            return State;
        }

        if (!AllItemsTerminal)
        {
            return State;
        }

        var succeeded = SucceededItemCount;
        var failed = FailedItemCount;

        if (failed == 0)
        {
            TransitionTo(
                SagaState.Completed,
                $"All {succeeded} item(s) succeeded",
                actor: null,
                correlationId,
                now);
            ClearTimeout(now);
            return State;
        }

        if (succeeded == 0)
        {
            TransitionTo(
                SagaState.Failed,
                $"All {failed} item(s) failed",
                actor: null,
                correlationId,
                now);
            ClearTimeout(now);
            return State;
        }

        // Forward recovery: keep successes; leave partial for selective retry (Phase 7).
        TransitionTo(
            SagaState.PartiallyCompleted,
            $"Partial fulfillment succeeded={succeeded} failed={failed} (forward recovery)",
            actor: null,
            correlationId,
            now);
        ClearTimeout(now);
        return State;
    }
    private void SeedItemOutcomes(
        IEnumerable<(RequestItemId ItemId, EntitlementId EntitlementId, LogicalOperationId LogicalOperationId)> items,
        DateTimeOffset now)
    {
        var list = items?.ToList()
            ?? throw new DomainException("Fulfillment items are required.");

        if (list.Count == 0)
        {
            throw new DomainException("At least one fulfillment item is required.");
        }

        if (_itemOutcomes.Count > 0)
        {
            return;
        }

        foreach (var item in list)
        {
            _itemOutcomes.Add(SagaItemOutcome.CreatePending(
                item.ItemId,
                item.EntitlementId,
                item.LogicalOperationId,
                now));
        }
    }

    private SagaItemOutcome FindItem(LogicalOperationId logicalOperationId)
    {
        var item = _itemOutcomes.SingleOrDefault(i => i.LogicalOperationId.Equals(logicalOperationId));
        if (item is null)
        {
            throw new DomainException(
                $"Saga has no item outcome for logical operation '{logicalOperationId.Value}'.");
        }

        return item;
    }

    private void TryAggregateAfterItemUpdate(string? correlationId, DateTimeOffset now)
    {
        if (State is not (SagaState.Processing or SagaState.PartiallyCompleted or SagaState.ReconciliationRequired))
        {
            if (State == SagaState.QueuedForFulfillment && AllItemsTerminal)
            {
                // Results arrived before MarkProcessing — advance then aggregate.
                TransitionTo(
                    SagaState.Processing,
                    "Results received; advancing to Processing before aggregation",
                    actor: null,
                    correlationId,
                    now);
            }
            else if (State != SagaState.Processing)
            {
                return;
            }
        }

        if (AllItemsTerminal || _itemOutcomes.Any(i => i.Status == SagaItemOutcomeStatus.Uncertain))
        {
            AggregateStatus(correlationId, now);
        }
    }

    /// <summary>
    /// Re-opens a terminal Failed saga after a corrective Succeeded repair.
    /// </summary>
    private void ReaggregateAfterFailedRepair(string? correlationId, DateTimeOffset now)
    {
        if (!AllItemsTerminal)
        {
            return;
        }

        var succeeded = SucceededItemCount;
        var failed = FailedItemCount;

        if (failed == 0 && succeeded > 0)
        {
            TransitionTo(
                SagaState.Completed,
                "Corrective repair: all items Succeeded after provider ConfirmedApplied",
                actor: "reconciliation",
                correlationId,
                now);
            ClearTimeout(now);
            return;
        }

        if (succeeded > 0)
        {
            TransitionTo(
                SagaState.PartiallyCompleted,
                "Corrective repair: mixed outcomes after provider ConfirmedApplied",
                actor: "reconciliation",
                correlationId,
                now);
            ClearTimeout(now);
        }
    }
}
