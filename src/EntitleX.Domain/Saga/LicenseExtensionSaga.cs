using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Saga;

/// <summary>
/// Durable centralized saga for a single license-extension request.
/// Enforces approval ordering, validates transitions, tracks item outcomes,
/// and aggregates terminal status using forward recovery only.
/// </summary>
public sealed class LicenseExtensionSaga : AggregateRoot
{
    private static readonly IReadOnlyDictionary<SagaState, SagaState[]> AllowedTransitions =
        new Dictionary<SagaState, SagaState[]>
        {
            [SagaState.Requested] =
            [
                SagaState.PendingRenewalManagerApproval,
                SagaState.Approved,
                SagaState.Rejected,
                SagaState.Escalated
            ],
            [SagaState.PendingRenewalManagerApproval] =
            [
                SagaState.PendingDealDeskApproval,
                SagaState.Approved,
                SagaState.Rejected,
                SagaState.Escalated
            ],
            [SagaState.PendingDealDeskApproval] =
            [
                SagaState.Approved,
                SagaState.Rejected,
                SagaState.Escalated
            ],
            [SagaState.Approved] =
            [
                SagaState.QueuedForFulfillment
            ],
            [SagaState.QueuedForFulfillment] =
            [
                SagaState.Processing,
                SagaState.ReconciliationRequired,
                SagaState.Escalated
            ],
            [SagaState.Processing] =
            [
                SagaState.PartiallyCompleted,
                SagaState.Completed,
                SagaState.Failed,
                SagaState.ReconciliationRequired
            ],
            [SagaState.PartiallyCompleted] =
            [
                SagaState.Completed,
                SagaState.Failed,
                SagaState.ReconciliationRequired,
                SagaState.Processing
            ],
            [SagaState.ReconciliationRequired] =
            [
                SagaState.Processing,
                SagaState.PartiallyCompleted,
                SagaState.Completed,
                SagaState.Failed,
                SagaState.Escalated
            ],
            [SagaState.Escalated] =
            [
                SagaState.PendingRenewalManagerApproval,
                SagaState.PendingDealDeskApproval,
                SagaState.Approved,
                SagaState.Rejected,
                SagaState.QueuedForFulfillment,
                SagaState.Processing,
                SagaState.ReconciliationRequired
            ],
            [SagaState.Completed] = [],
            // Corrective repair only: provider ConfirmedApplied after a false Failed.
            [SagaState.Failed] =
            [
                SagaState.PartiallyCompleted,
                SagaState.Completed
            ],
            [SagaState.Rejected] = []
        };

    private readonly List<SagaTransition> _transitions = [];
    private readonly List<SagaItemOutcome> _itemOutcomes = [];

    private LicenseExtensionSaga()
    {
        // EF Core
    }

    private LicenseExtensionSaga(
        Guid id,
        RequestId requestId,
        ApprovalBand approvalBand,
        bool requiresDealDesk,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        RequestId = requestId;
        ApprovalBand = approvalBand;
        RequiresDealDesk = requiresDealDesk;
        State = SagaState.Requested;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        TimeoutKind = SagaTimeoutKind.None;
        RenewalManagerApproved = false;
        DealDeskApproved = false;
    }

    public Guid Id { get; private set; }

    public RequestId RequestId { get; private set; }

    public SagaState State { get; private set; }

    public ApprovalBand ApprovalBand { get; private set; }

    /// <summary>True when the band requires Renewal Manager then Deal Desk.</summary>
    public bool RequiresDealDesk { get; private set; }

    public bool RenewalManagerApproved { get; private set; }

    public bool DealDeskApproved { get; private set; }

    public SagaTimeoutKind TimeoutKind { get; private set; }

    public DateTimeOffset? TimeoutDeadlineUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token (persisted as row_version).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyList<SagaTransition> Transitions => _transitions.AsReadOnly();

    public IReadOnlyList<SagaItemOutcome> ItemOutcomes => _itemOutcomes.AsReadOnly();

    public int SucceededItemCount =>
        _itemOutcomes.Count(i => i.Status == SagaItemOutcomeStatus.Succeeded);

    public int FailedItemCount =>
        _itemOutcomes.Count(i => i.Status == SagaItemOutcomeStatus.Failed);

    public int PendingItemCount =>
        _itemOutcomes.Count(i => i.Status is SagaItemOutcomeStatus.Pending or SagaItemOutcomeStatus.Uncertain);

    public bool AllItemsTerminal =>
        _itemOutcomes.Count > 0 &&
        _itemOutcomes.All(i => i.Status is SagaItemOutcomeStatus.Succeeded or SagaItemOutcomeStatus.Failed);

    /// <summary>
    /// Opens a saga in <see cref="SagaState.Requested"/> and immediately routes
    /// into the first approval / auto-approve / reject state for the band.
    /// </summary>
    public static LicenseExtensionSaga Start(
        RequestId requestId,
        ApprovalBand approvalBand,
        DateTimeOffset? createdAtUtc = null,
        string? correlationId = null)
    {
        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        var requiresDealDesk = approvalBand == ApprovalBand.RenewalManagerThenDealDesk;

        var saga = new LicenseExtensionSaga(
            Guid.NewGuid(),
            requestId,
            approvalBand,
            requiresDealDesk,
            now);

        saga.RouteInitialBand(correlationId, now);
        return saga;
    }

    /// <summary>
    /// Records Renewal Manager approval. Enforces ordering: Deal Desk cannot
    /// be skipped when required; Deal Desk cannot run before RM.
    /// </summary>
    public void RecordRenewalManagerApproval(
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (SagaState.PendingRenewalManagerApproval or SagaState.Escalated))
        {
            throw new DomainException(
                $"Renewal Manager approval is not valid from saga state '{State}'.");
        }

        if (State == SagaState.Escalated &&
            !_transitions.Any(t => t.ToState == SagaState.PendingRenewalManagerApproval ||
                                   t.FromState == SagaState.PendingRenewalManagerApproval))
        {
            // Escalated before RM could act — resume into RM path first.
        }

        RenewalManagerApproved = true;

        if (RequiresDealDesk)
        {
            TransitionTo(
                SagaState.PendingDealDeskApproval,
                "Renewal Manager approved; Deal Desk required next",
                actor,
                correlationId,
                now);
            ScheduleTimeout(SagaTimeoutKind.ApprovalPending, now.AddDays(3), now);
        }
        else
        {
            TransitionTo(
                SagaState.Approved,
                "Renewal Manager approved (final)",
                actor,
                correlationId,
                now);
            ClearTimeout(now);
        }
    }

    /// <summary>
    /// Records Deal Desk approval. Requires prior Renewal Manager approval when
    /// the band mandates sequential stages.
    /// </summary>
    public void RecordDealDeskApproval(
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (!RequiresDealDesk)
        {
            throw new DomainException(
                "Deal Desk approval is not part of this saga's approval band.");
        }

        if (!RenewalManagerApproved)
        {
            throw new DomainException(
                "Deal Desk cannot approve before Renewal Manager (approval ordering).");
        }

        if (State is not (SagaState.PendingDealDeskApproval or SagaState.Escalated))
        {
            throw new DomainException(
                $"Deal Desk approval is not valid from saga state '{State}'.");
        }

        DealDeskApproved = true;
        TransitionTo(
            SagaState.Approved,
            "Deal Desk approved (final)",
            actor,
            correlationId,
            now);
        ClearTimeout(now);
    }

    public void RecordRejection(
        string? actor = null,
        string? correlationId = null,
        string? reason = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (
            SagaState.Requested or
            SagaState.PendingRenewalManagerApproval or
            SagaState.PendingDealDeskApproval or
            SagaState.Escalated))
        {
            throw new DomainException(
                $"Rejection is not valid from saga state '{State}'.");
        }

        TransitionTo(
            SagaState.Rejected,
            reason ?? "Request rejected during approval",
            actor,
            correlationId,
            now);
        ClearTimeout(now);
    }

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

    public void MarkReconciliationRequired(
        string reason,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (
            SagaState.QueuedForFulfillment or
            SagaState.Processing or
            SagaState.PartiallyCompleted or
            SagaState.Escalated))
        {
            throw new DomainException(
                $"Cannot mark reconciliation required from saga state '{State}'.");
        }

        foreach (var pending in _itemOutcomes.Where(i => i.Status == SagaItemOutcomeStatus.Pending))
        {
            pending.MarkUncertain(reason, now);
        }

        TransitionTo(
            SagaState.ReconciliationRequired,
            reason,
            actor: null,
            correlationId,
            now);
    }

    public void Escalate(
        string reason,
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        ApplyConfiguredTerminalState(
            SagaState.Escalated,
            reason,
            actor,
            correlationId,
            atUtc);
    }

    /// <summary>
    /// Moves the saga to a configured terminal/escalation state after a stalled
    /// approval reminder. The target is supplied by host configuration (not a
    /// hardcoded historical name in the reminder Lambda).
    /// Idempotent when already in the target state.
    /// </summary>
    public void ApplyConfiguredTerminalState(
        SagaState terminalState,
        string reason,
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State == terminalState)
        {
            ClearTimeout(now);
            return;
        }

        if (State is SagaState.Completed or SagaState.Failed or SagaState.Rejected)
        {
            throw new DomainException($"Cannot apply terminal state '{terminalState}' from '{State}'.");
        }

        TransitionTo(
            terminalState,
            reason,
            actor,
            correlationId,
            now);
        ClearTimeout(now);
    }

    /// <summary>
    /// Timeout hook entry point. Approval timeouts escalate; fulfillment timeouts
    /// mark reconciliation required for pending items.
    /// </summary>
    public void ApplyTimeout(
        DateTimeOffset nowUtc,
        string? correlationId = null)
    {
        if (TimeoutKind == SagaTimeoutKind.None || TimeoutDeadlineUtc is null)
        {
            return;
        }

        if (nowUtc < TimeoutDeadlineUtc.Value)
        {
            return;
        }

        switch (TimeoutKind)
        {
            case SagaTimeoutKind.ApprovalPending:
                Escalate(
                    $"Approval timeout elapsed at {TimeoutDeadlineUtc:u}",
                    actor: "timeout-hook",
                    correlationId,
                    nowUtc);
                break;

            case SagaTimeoutKind.FulfillmentProcessing:
                MarkReconciliationRequired(
                    $"Fulfillment timeout elapsed at {TimeoutDeadlineUtc:u}",
                    correlationId,
                    nowUtc);
                break;

            default:
                throw new DomainException($"Unhandled timeout kind '{TimeoutKind}'.");
        }
    }

    public void ScheduleTimeout(
        SagaTimeoutKind kind,
        DateTimeOffset deadlineUtc,
        DateTimeOffset? atUtc = null)
    {
        if (kind == SagaTimeoutKind.None)
        {
            ClearTimeout(atUtc ?? DateTimeOffset.UtcNow);
            return;
        }

        TimeoutKind = kind;
        TimeoutDeadlineUtc = deadlineUtc;
        UpdatedAtUtc = atUtc ?? DateTimeOffset.UtcNow;
    }

    public void ResumeFromEscalation(
        SagaState target,
        string reason,
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        if (State != SagaState.Escalated)
        {
            throw new DomainException("ResumeFromEscalation requires Escalated state.");
        }

        var now = atUtc ?? DateTimeOffset.UtcNow;
        TransitionTo(target, reason, actor, correlationId, now);
    }

    private void RouteInitialBand(string? correlationId, DateTimeOffset now)
    {
        switch (ApprovalBand)
        {
            case ApprovalBand.AutoApprove:
                TransitionTo(
                    SagaState.Approved,
                    "Auto-approved by day-band policy",
                    actor: "system",
                    correlationId,
                    now);
                break;

            case ApprovalBand.RenewalManagerOnly:
                TransitionTo(
                    SagaState.PendingRenewalManagerApproval,
                    "Awaiting Renewal Manager approval",
                    actor: "system",
                    correlationId,
                    now);
                ScheduleTimeout(SagaTimeoutKind.ApprovalPending, now.AddDays(3), now);
                break;

            case ApprovalBand.RenewalManagerThenDealDesk:
                TransitionTo(
                    SagaState.PendingRenewalManagerApproval,
                    "Awaiting Renewal Manager then Deal Desk",
                    actor: "system",
                    correlationId,
                    now);
                ScheduleTimeout(SagaTimeoutKind.ApprovalPending, now.AddDays(3), now);
                break;

            case ApprovalBand.Unsupported:
                TransitionTo(
                    SagaState.Rejected,
                    "Extension length unsupported by policy",
                    actor: "system",
                    correlationId,
                    now);
                break;

            default:
                throw new DomainException($"Unknown approval band '{ApprovalBand}'.");
        }
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

    private bool IsApprovedPathComplete() =>
        ApprovalBand switch
        {
            ApprovalBand.AutoApprove => true,
            ApprovalBand.RenewalManagerOnly => RenewalManagerApproved,
            ApprovalBand.RenewalManagerThenDealDesk => RenewalManagerApproved && DealDeskApproved,
            _ => false
        };

    private void ClearTimeout(DateTimeOffset now)
    {
        TimeoutKind = SagaTimeoutKind.None;
        TimeoutDeadlineUtc = null;
        UpdatedAtUtc = now;
    }

    private void EnsureNotTerminal()
    {
        if (State is SagaState.Completed or SagaState.Failed or SagaState.Rejected)
        {
            throw new DomainException($"Saga is terminal in state '{State}'.");
        }
    }

    private void TransitionTo(
        SagaState next,
        string reason,
        string? actor,
        string? correlationId,
        DateTimeOffset atUtc)
    {
        if (State == next)
        {
            UpdatedAtUtc = atUtc;
            return;
        }

        if (!AllowedTransitions.TryGetValue(State, out var allowed) || !allowed.Contains(next))
        {
            throw new DomainException(
                $"Cannot transition saga from '{State}' to '{next}'.");
        }

        var from = State;
        State = next;
        UpdatedAtUtc = atUtc;
        _transitions.Add(SagaTransition.Create(
            from,
            next,
            reason,
            actor,
            correlationId,
            atUtc));
    }
}
