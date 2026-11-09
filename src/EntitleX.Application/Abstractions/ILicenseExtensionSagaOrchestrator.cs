using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Application-level saga coordinator: keeps the durable saga aligned with
/// approvals, fulfillment dispatch, and provider completion/failure events.
/// </summary>
public interface ILicenseExtensionSagaOrchestrator
{
    Task<LicenseExtensionSaga> StartForRequestAsync(
        LicenseExtensionRequest request,
        ApprovalBand band,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task OnApprovalDecisionAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalDecision decision,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task OnRejectedWithoutDecisionAsync(
        LicenseExtensionRequest request,
        string reason,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task DispatchFulfillmentAsync(
        LicenseExtensionRequest request,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatch using a saga already started in this unit of work (avoids EF
    /// re-query of Added-but-unsaved aggregates). Prefer this after
    /// <see cref="StartForRequestAsync"/>.
    /// </summary>
    Task DispatchFulfillmentAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default);

    Task OnFulfillmentDispatchedAsync(
        RequestId requestId,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task OnItemCompletedAsync(
        RequestId requestId,
        LogicalOperationId logicalOperationId,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task OnItemFailedAsync(
        RequestId requestId,
        LogicalOperationId logicalOperationId,
        string? errorCode,
        string? errorMessage,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task ApplyElapsedTimeoutsAsync(
        DateTimeOffset nowUtc,
        int take = 50,
        CancellationToken cancellationToken = default);
}
