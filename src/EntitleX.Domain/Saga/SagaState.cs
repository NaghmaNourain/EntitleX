namespace EntitleX.Domain.Saga;

/// <summary>
/// Centralized license-extension saga states.
/// Request status <c>InFulfillment</c> is not a separate saga value — it is the
/// coarse request projection of <see cref="QueuedForFulfillment"/> and
/// <see cref="Processing"/> (see docs/saga-workflow.md).
/// </summary>
public enum SagaState
{
    Requested = 0,
    PendingRenewalManagerApproval = 1,
    PendingDealDeskApproval = 2,
    Approved = 3,
    QueuedForFulfillment = 4,
    Processing = 5,
    PartiallyCompleted = 6,
    Completed = 7,
    Failed = 8,
    ReconciliationRequired = 9,
    Escalated = 10,
    Rejected = 11
}
