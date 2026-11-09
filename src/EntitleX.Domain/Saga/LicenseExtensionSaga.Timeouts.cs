using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Saga;

public sealed partial class LicenseExtensionSaga
{
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
}
