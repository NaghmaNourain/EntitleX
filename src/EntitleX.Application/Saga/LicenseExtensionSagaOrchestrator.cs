using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Saga;

public sealed class LicenseExtensionSagaOrchestrator : ILicenseExtensionSagaOrchestrator
{
    private readonly ILicenseExtensionSagaRepository _sagaRepository;
    private readonly ILicenseExtensionRequestRepository _requestRepository;
    private readonly IFulfillmentOutboxService _fulfillmentOutbox;
    private readonly ISagaTimeoutHook _timeoutHook;
    private readonly ILogger<LicenseExtensionSagaOrchestrator> _logger;

    public LicenseExtensionSagaOrchestrator(
        ILicenseExtensionSagaRepository sagaRepository,
        ILicenseExtensionRequestRepository requestRepository,
        IFulfillmentOutboxService fulfillmentOutbox,
        ISagaTimeoutHook timeoutHook,
        ILogger<LicenseExtensionSagaOrchestrator> logger)
    {
        _sagaRepository = sagaRepository;
        _requestRepository = requestRepository;
        _fulfillmentOutbox = fulfillmentOutbox;
        _timeoutHook = timeoutHook;
        _logger = logger;
    }

    public async Task<LicenseExtensionSaga> StartForRequestAsync(
        LicenseExtensionRequest request,
        ApprovalBand band,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SagaTelemetry.StartTransition("start");
        var existing = await _sagaRepository.GetByRequestIdAsync(request.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        // Routes Requested → Approved (auto), Pending* (human), or Rejected (unsupported).
        var saga = LicenseExtensionSaga.Start(request.Id, band, correlationId: correlationId);
        await _sagaRepository.AddAsync(saga, cancellationToken);
        SagaTelemetry.RecordTransition("None", saga.State.ToString());
        return saga;
    }

    public async Task OnApprovalDecisionAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalDecision decision,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SagaTelemetry.StartTransition("approval_decision");
        var saga = await RequireSagaAsync(request.Id, cancellationToken);
        var from = saga.State.ToString();

        if (decision.Outcome == ApprovalDecisionOutcome.Rejected)
        {
            saga.RecordRejection(
                decision.ActorUserId,
                correlationId,
                reason: decision.Comment ?? "Rejected by approver");
            SagaTelemetry.RecordTransition(from, saga.State.ToString());
            return;
        }

        if (decision.StageRole == ApprovalStageRole.RenewalManager)
        {
            saga.RecordRenewalManagerApproval(decision.ActorUserId, correlationId);
        }
        else if (decision.StageRole == ApprovalStageRole.DealDeskManager)
        {
            saga.RecordDealDeskApproval(decision.ActorUserId, correlationId);
        }
        else
        {
            throw new DomainException($"Unhandled approval stage role '{decision.StageRole}'.");
        }

        SagaTelemetry.RecordTransition(from, saga.State.ToString());

        if (approvalCase.Status == ApprovalCaseStatus.Approved && saga.State == SagaState.Approved)
        {
            // Fulfillment dispatch is owned by the caller after RecordApproval on the request.
        }
    }

    public async Task OnRejectedWithoutDecisionAsync(
        LicenseExtensionRequest request,
        string reason,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var saga = await RequireSagaAsync(request.Id, cancellationToken);
        if (saga.State != SagaState.Rejected)
        {
            saga.RecordRejection(actor: "system", correlationId, reason);
        }
    }

    public Task DispatchFulfillmentAsync(
        LicenseExtensionRequest request,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default) =>
        DispatchFulfillmentCoreAsync(request, saga: null, correlationId, causationId, cancellationToken);

    public Task DispatchFulfillmentAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default) =>
        DispatchFulfillmentCoreAsync(request, saga, correlationId, causationId, cancellationToken);

    private async Task DispatchFulfillmentCoreAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga? saga,
        Guid? correlationId,
        Guid? causationId,
        CancellationToken cancellationToken)
    {
        using var activity = SagaTelemetry.StartTransition("dispatch_fulfillment");
        saga ??= await RequireSagaAsync(request.Id, cancellationToken);
        var from = saga.State.ToString();

        if (request.Status != LicenseExtensionRequestStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Request '{request.Id}' must be Approved before fulfillment dispatch (was '{request.Status}').");
        }

        if (saga.State != SagaState.Approved)
        {
            throw new InvalidOperationException(
                $"Saga for request '{request.Id}' must be Approved before fulfillment (was '{saga.State}').");
        }

        await _fulfillmentOutbox.EnqueueAfterFinalApprovalAsync(
            request,
            correlationId,
            causationId,
            cancellationToken);

        var items = request.Items.Select(i => (
            i.Id,
            i.EntitlementId,
            i.LogicalOperationId));

        saga.QueueFulfillment(
            items,
            correlationId?.ToString("D"));
        SagaTelemetry.RecordTransition(from, saga.State.ToString());
    }

    public async Task OnFulfillmentDispatchedAsync(
        RequestId requestId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SagaTelemetry.StartTransition("fulfillment_dispatched");
        var saga = await _sagaRepository.GetByRequestIdAsync(requestId, cancellationToken);
        if (saga is null)
        {
            _logger.LogWarning("No saga for request {RequestId} on fulfillment dispatch", requestId.Value);
            return;
        }

        var from = saga.State.ToString();
        if (saga.State == SagaState.QueuedForFulfillment)
        {
            saga.MarkProcessing(correlationId);
            SagaTelemetry.RecordTransition(from, saga.State.ToString());
        }
        else if (saga.State == SagaState.Processing)
        {
            // Idempotent replay.
        }
        else
        {
            _logger.LogWarning(
                "Unexpected saga state {State} for request {RequestId} when marking processing",
                saga.State,
                requestId.Value);
        }
    }

    public async Task OnItemCompletedAsync(
        RequestId requestId,
        LogicalOperationId logicalOperationId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SagaTelemetry.StartTransition("item_completed");
        var saga = await RequireSagaAsync(requestId, cancellationToken);
        var from = saga.State.ToString();
        saga.RecordItemSucceeded(logicalOperationId, correlationId);
        if (from != saga.State.ToString())
        {
            SagaTelemetry.RecordTransition(from, saga.State.ToString());
        }

        await SyncRequestFulfillmentAsync(requestId, saga, cancellationToken);
    }

    public async Task OnItemFailedAsync(
        RequestId requestId,
        LogicalOperationId logicalOperationId,
        string? errorCode,
        string? errorMessage,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = SagaTelemetry.StartTransition("item_failed");
        var saga = await RequireSagaAsync(requestId, cancellationToken);
        var from = saga.State.ToString();
        saga.RecordItemFailed(logicalOperationId, errorCode, errorMessage, correlationId);
        if (from != saga.State.ToString())
        {
            SagaTelemetry.RecordTransition(from, saga.State.ToString());
        }

        await SyncRequestFulfillmentAsync(requestId, saga, cancellationToken);
    }

    public async Task ApplyElapsedTimeoutsAsync(
        DateTimeOffset nowUtc,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var timedOut = await _sagaRepository.ListTimedOutAsync(nowUtc, take, cancellationToken);
        foreach (var saga in timedOut)
        {
            await _timeoutHook.HandleAsync(saga, cancellationToken);
        }
    }

    private async Task SyncRequestFulfillmentAsync(
        RequestId requestId,
        LicenseExtensionSaga saga,
        CancellationToken cancellationToken)
    {
        if (saga.State is not (
            SagaState.Completed or
            SagaState.Failed or
            SagaState.PartiallyCompleted or
            SagaState.ReconciliationRequired))
        {
            return;
        }

        var request = await _requestRepository.GetByIdAsync(requestId, cancellationToken);
        if (request is null)
        {
            return;
        }

        if (request.Status is not (
            LicenseExtensionRequestStatus.InFulfillment or
            LicenseExtensionRequestStatus.PartiallyFulfilled or
            LicenseExtensionRequestStatus.Failed))
        {
            return;
        }

        // ReconciliationRequired projects to PartiallyFulfilled when any success exists;
        // otherwise leave request InFulfillment until outcomes clarify (Phase 8).
        if (saga.State == SagaState.ReconciliationRequired)
        {
            if (saga.SucceededItemCount > 0 && saga.FailedItemCount + saga.PendingItemCount > 0)
            {
                try
                {
                    request.CompleteFulfillment(saga.SucceededItemCount, Math.Max(saga.FailedItemCount, 1));
                }
                catch (DomainException ex)
                {
                    _logger.LogDebug(ex, "Request status already settled for {RequestId}", requestId.Value);
                }
            }

            return;
        }

        try
        {
            request.CompleteFulfillment(saga.SucceededItemCount, saga.FailedItemCount);
            var outcome = saga.State switch
            {
                SagaState.Completed => "success",
                SagaState.PartiallyCompleted => "partial",
                SagaState.Failed => "failed",
                _ => "other"
            };
            SagaTelemetry.RecordFulfillmentOutcome(outcome);
        }
        catch (DomainException ex)
        {
            _logger.LogDebug(ex, "Request status already settled for {RequestId}", requestId.Value);
        }
    }

    private async Task<LicenseExtensionSaga> RequireSagaAsync(
        RequestId requestId,
        CancellationToken cancellationToken)
    {
        // Repository checks ChangeTracker.Local for Added-but-unsaved sagas.
        return await _sagaRepository.GetByRequestIdAsync(requestId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Saga for request '{requestId.Value}' was not found.");
    }
}
