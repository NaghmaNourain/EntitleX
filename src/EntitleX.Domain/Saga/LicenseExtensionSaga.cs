using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Saga;

/// <summary>
/// Durable centralized saga for a single license-extension request.
/// Enforces approval ordering, validates transitions, tracks item outcomes,
/// and aggregates terminal status using forward recovery only.
/// Split across partials: Approvals / Fulfillment / Timeouts (no behavior change).
/// </summary>
public sealed partial class LicenseExtensionSaga : AggregateRoot
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
