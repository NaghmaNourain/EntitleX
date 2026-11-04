using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.Saga;

/// <summary>
/// Maps centralized saga states onto the coarser
/// <see cref="LicenseExtensionRequestStatus"/> values used by the request aggregate.
/// </summary>
public static class SagaRequestStatusMap
{
    /// <summary>
    /// Canonical projection. <see cref="SagaState.QueuedForFulfillment"/> and
    /// <see cref="SagaState.Processing"/> both project to
    /// <see cref="LicenseExtensionRequestStatus.InFulfillment"/> — that request
    /// status is the already-shipped Phase 5 name for active fulfillment work.
    /// </summary>
    public static LicenseExtensionRequestStatus ToRequestStatus(SagaState state) =>
        state switch
        {
            SagaState.Requested => LicenseExtensionRequestStatus.Submitted,
            SagaState.PendingRenewalManagerApproval => LicenseExtensionRequestStatus.PendingApproval,
            SagaState.PendingDealDeskApproval => LicenseExtensionRequestStatus.PendingApproval,
            SagaState.Approved => LicenseExtensionRequestStatus.Approved,
            SagaState.QueuedForFulfillment => LicenseExtensionRequestStatus.InFulfillment,
            SagaState.Processing => LicenseExtensionRequestStatus.InFulfillment,
            SagaState.PartiallyCompleted => LicenseExtensionRequestStatus.PartiallyFulfilled,
            SagaState.Completed => LicenseExtensionRequestStatus.Fulfilled,
            SagaState.Failed => LicenseExtensionRequestStatus.Failed,
            SagaState.ReconciliationRequired => LicenseExtensionRequestStatus.PartiallyFulfilled,
            SagaState.Escalated => LicenseExtensionRequestStatus.PendingApproval,
            SagaState.Rejected => LicenseExtensionRequestStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown saga state.")
        };

    /// <summary>
    /// Whether the request aggregate's <c>InFulfillment</c> status corresponds to
    /// the given saga state (queued or actively processing).
    /// </summary>
    public static bool IsInFulfillmentProjection(SagaState state) =>
        state is SagaState.QueuedForFulfillment or SagaState.Processing;
}
