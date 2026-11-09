using EntitleX.Application.Abstractions;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Reconciliation;

/// <summary>
/// Finds uncertain / missing / mismatched outcomes, probes providers by
/// LogicalOperationId, and repairs local saga state. Never blind-retries
/// unknown external effects.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly ILicenseExtensionSagaRepository _sagaRepository;
    private readonly ILicenseExtensionRequestRepository _requestRepository;
    private readonly ILicenseExtensionSagaOrchestrator _sagaOrchestrator;
    private readonly IReadOnlyDictionary<ProviderSystem, IProviderOperationStatusClient> _providerClients;
    private readonly ILocalFulfillmentOutcomeStore _localOutcomes;
    private readonly IAuditEventStore _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        ILicenseExtensionSagaRepository sagaRepository,
        ILicenseExtensionRequestRepository requestRepository,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        IEnumerable<IProviderOperationStatusClient> providerClients,
        ILocalFulfillmentOutcomeStore localOutcomes,
        IAuditEventStore audit,
        IUnitOfWork unitOfWork,
        ILogger<ReconciliationService> logger)
    {
        _sagaRepository = sagaRepository;
        _requestRepository = requestRepository;
        _sagaOrchestrator = sagaOrchestrator;
        _providerClients = providerClients.ToDictionary(c => c.Provider);
        _localOutcomes = localOutcomes;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReconciliationRunResult>> RunBatchAsync(
        int take = 50,
        string actor = "reconciliation-worker",
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ReconciliationTelemetry.StartPass("batch");
        await _sagaOrchestrator.ApplyElapsedTimeoutsAsync(
            DateTimeOffset.UtcNow,
            take,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var candidates = await _sagaRepository.ListRequiringReconciliationAsync(
            take,
            cancellationToken);
        ReconciliationTelemetry.SetBacklog(candidates.Count);

        var results = new List<ReconciliationRunResult>(candidates.Count);
        foreach (var saga in candidates)
        {
            results.Add(await ReconcileRequestAsync(
                saga.RequestId,
                actor,
                correlationId,
                cancellationToken));
        }

        return results;
    }

    public async Task<ReconciliationRunResult> ReconcileRequestAsync(
        RequestId requestId,
        string actor,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = ReconciliationTelemetry.StartPass("request");
        var saga = await _sagaRepository.GetByRequestIdAsync(requestId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No saga found for request '{requestId.Value}'.");

        var request = await _requestRepository.GetByIdAsync(requestId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Request '{requestId.Value}' was not found.");

        await _audit.AppendAsync(
            new AuditEventRecord(
                Guid.NewGuid(),
                AuditEventTypes.ReconciliationPassStarted,
                requestId,
                LogicalOperationId: null,
                actor,
                $"Reconciliation pass started (saga={saga.State})",
                correlationId,
                DateTimeOffset.UtcNow),
            cancellationToken);

        var itemResults = new List<ReconciliationItemResult>();
        var itemsByLogical = request.Items.ToDictionary(i => i.LogicalOperationId.Value);

        var candidates = SelectCandidateOutcomes(saga, await _localOutcomes.ListByRequestAsync(
            requestId,
            cancellationToken));

        foreach (var outcome in candidates)
        {
            if (!itemsByLogical.TryGetValue(outcome.LogicalOperationId.Value, out var requestItem))
            {
                itemResults.Add(new ReconciliationItemResult(
                    outcome.LogicalOperationId.Value,
                    "Unknown",
                    ReconciliationItemAction.KeptUnknown,
                    "No request item for logical operation; left unresolved."));
                continue;
            }

            itemResults.Add(await ReconcileItemAsync(
                request,
                saga,
                requestItem,
                outcome,
                actor,
                correlationId,
                cancellationToken));
        }

        // Re-aggregate after all item updates.
        if (saga.State == SagaState.ReconciliationRequired
            || saga.ItemOutcomes.Any(o => o.Status == SagaItemOutcomeStatus.Uncertain))
        {
            saga.AggregateStatus(correlationId);
        }

        await SyncRequestStatusAsync(request, saga, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var stillRequired = saga.State == SagaState.ReconciliationRequired
            || saga.ItemOutcomes.Any(o => o.Status == SagaItemOutcomeStatus.Uncertain);

        await _audit.AppendAsync(
            new AuditEventRecord(
                Guid.NewGuid(),
                AuditEventTypes.ReconciliationBatchCompleted,
                requestId,
                LogicalOperationId: null,
                actor,
                $"Reconciliation pass finished (saga={saga.State}, stillRequired={stillRequired}, items={itemResults.Count})",
                correlationId,
                DateTimeOffset.UtcNow),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var item in itemResults)
        {
            ReconciliationTelemetry.RecordAction(item.Action.ToString());
        }

        return new ReconciliationRunResult(
            requestId.Value,
            saga.State,
            itemResults,
            stillRequired);
    }

    private async Task<ReconciliationItemResult> ReconcileItemAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        LicenseExtensionRequestItem requestItem,
        SagaItemOutcome outcome,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var providerName = requestItem.Provider.ToString();

        if (!_providerClients.TryGetValue(requestItem.Provider, out var client))
        {
            saga.KeepItemUncertainFromReconciliation(
                outcome.LogicalOperationId,
                $"No provider status client for {providerName}",
                correlationId);
            await AppendAuditAsync(
                AuditEventTypes.ReconciliationItemStillUnknown,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                $"Missing provider client for {providerName}",
                correlationId,
                cancellationToken);
            return new ReconciliationItemResult(
                outcome.LogicalOperationId.Value,
                providerName,
                ReconciliationItemAction.KeptUnknown,
                "No provider status client registered.");
        }

        ProviderOperationStatus status;
        try
        {
            status = await client.GetByLogicalOperationIdAsync(
                outcome.LogicalOperationId,
                correlationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Provider probe failed for {LogicalOperationId} on {Provider}",
                outcome.LogicalOperationId.Value,
                providerName);

            saga.KeepItemUncertainFromReconciliation(
                outcome.LogicalOperationId,
                $"Provider probe error: {ex.Message}",
                correlationId);
            await AppendAuditAsync(
                AuditEventTypes.ReconciliationItemStillUnknown,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                $"Provider probe threw: {ex.Message}",
                correlationId,
                cancellationToken);
            return new ReconciliationItemResult(
                outcome.LogicalOperationId.Value,
                providerName,
                ReconciliationItemAction.KeptUnknown,
                "Provider probe failed; left Uncertain (no blind retry).");
        }

        return status.Kind switch
        {
            ProviderOperationLookupKind.ConfirmedApplied =>
                await ApplyConfirmedSuccessAsync(
                    request, saga, requestItem, outcome, status, actor, correlationId, cancellationToken),

            ProviderOperationLookupKind.ConfirmedFailed =>
                await ApplyConfirmedFailureAsync(
                    request, saga, requestItem, outcome, status, actor, correlationId, cancellationToken),

            ProviderOperationLookupKind.NotFound or ProviderOperationLookupKind.Unknown =>
                await KeepUnknownAsync(
                    request, saga, outcome, status, actor, correlationId, cancellationToken),

            _ => await KeepUnknownAsync(
                request, saga, outcome, status, actor, correlationId, cancellationToken)
        };
    }

    private async Task<ReconciliationItemResult> ApplyConfirmedSuccessAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        LicenseExtensionRequestItem requestItem,
        SagaItemOutcome outcome,
        ProviderOperationStatus status,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var providerName = requestItem.Provider.ToString();

        if (outcome.Status == SagaItemOutcomeStatus.Failed)
        {
            await AppendAuditAsync(
                AuditEventTypes.ReconciliationMismatchDetected,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                "Local Failed but provider Applied — correcting local to Succeeded",
                correlationId,
                cancellationToken);
            saga.CorrectItemSucceededFromReconciliation(outcome.LogicalOperationId, correlationId);
        }
        else if (outcome.Status != SagaItemOutcomeStatus.Succeeded)
        {
            saga.CorrectItemSucceededFromReconciliation(outcome.LogicalOperationId, correlationId);
        }

        await _localOutcomes.TryRecordAsync(
            new LocalFulfillmentOutcome(
                request.Id.Value,
                requestItem.Id.Value,
                outcome.LogicalOperationId.Value,
                requestItem.AttemptId.Value,
                requestItem.EntitlementId.Value,
                providerName,
                Succeeded: true,
                ErrorCode: null,
                ErrorMessage: null,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await AppendAuditAsync(
            AuditEventTypes.ReconciliationItemRepairedSuccess,
            request.Id,
            outcome.LogicalOperationId,
            actor,
            status.Detail ?? "Provider confirmed Applied; local repaired to Succeeded",
            correlationId,
            cancellationToken);

        return new ReconciliationItemResult(
            outcome.LogicalOperationId.Value,
            providerName,
            ReconciliationItemAction.RepairedSuccess,
            status.Detail ?? "Repaired to Succeeded from provider Applied.");
    }

    private async Task<ReconciliationItemResult> ApplyConfirmedFailureAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        LicenseExtensionRequestItem requestItem,
        SagaItemOutcome outcome,
        ProviderOperationStatus status,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var providerName = requestItem.Provider.ToString();

        // Forward recovery: never demote a local Succeeded.
        if (outcome.Status == SagaItemOutcomeStatus.Succeeded)
        {
            await AppendAuditAsync(
                AuditEventTypes.ReconciliationMismatchDetected,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                "Local Succeeded but provider Failed — keeping local Succeeded (forward recovery)",
                correlationId,
                cancellationToken);
            return new ReconciliationItemResult(
                outcome.LogicalOperationId.Value,
                providerName,
                ReconciliationItemAction.MismatchNoted,
                "Local success retained despite provider Failed record.");
        }

        if (outcome.Status != SagaItemOutcomeStatus.Failed)
        {
            saga.RecordItemFailed(
                outcome.LogicalOperationId,
                status.FailureReason ?? "PROVIDER_FAILED",
                status.Detail ?? "Provider confirmed definitive failure",
                correlationId);
        }

        await _localOutcomes.TryRecordAsync(
            new LocalFulfillmentOutcome(
                request.Id.Value,
                requestItem.Id.Value,
                outcome.LogicalOperationId.Value,
                requestItem.AttemptId.Value,
                requestItem.EntitlementId.Value,
                providerName,
                Succeeded: false,
                ErrorCode: status.FailureReason ?? "PROVIDER_FAILED",
                ErrorMessage: status.Detail,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await AppendAuditAsync(
            AuditEventTypes.ReconciliationItemMarkedFailed,
            request.Id,
            outcome.LogicalOperationId,
            actor,
            "Provider confirmed failure; marked Failed (retry-eligible)",
            correlationId,
            cancellationToken);

        return new ReconciliationItemResult(
            outcome.LogicalOperationId.Value,
            providerName,
            ReconciliationItemAction.MarkedFailedRetryEligible,
            status.Detail ?? "Marked Failed; selective retry eligible.");
    }

    private async Task<ReconciliationItemResult> KeepUnknownAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        SagaItemOutcome outcome,
        ProviderOperationStatus status,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var detail = status.Detail
            ?? (status.Kind == ProviderOperationLookupKind.NotFound
                ? "Provider has no AppliedLicenseOperation; left Uncertain (no blind retry)"
                : "Provider status unknown; left Uncertain (no blind retry)");

        // Do not demote Succeeded; do not invent Failed on NotFound.
        if (outcome.Status is SagaItemOutcomeStatus.Pending or SagaItemOutcomeStatus.Uncertain)
        {
            saga.KeepItemUncertainFromReconciliation(
                outcome.LogicalOperationId,
                detail,
                correlationId);
        }

        await AppendAuditAsync(
            AuditEventTypes.ReconciliationItemStillUnknown,
            request.Id,
            outcome.LogicalOperationId,
            actor,
            detail,
            correlationId,
            cancellationToken);

        return new ReconciliationItemResult(
            outcome.LogicalOperationId.Value,
            status.Provider.ToString(),
            ReconciliationItemAction.KeptUnknown,
            detail);
    }

    private static IReadOnlyList<SagaItemOutcome> SelectCandidateOutcomes(
        LicenseExtensionSaga saga,
        IReadOnlyList<LocalFulfillmentOutcome> localOutcomes)
    {
        var localByLogical = localOutcomes.ToDictionary(o => o.LogicalOperationId);
        var selected = new List<SagaItemOutcome>();

        foreach (var outcome in saga.ItemOutcomes)
        {
            if (outcome.Status is SagaItemOutcomeStatus.Uncertain or SagaItemOutcomeStatus.Pending)
            {
                selected.Add(outcome);
                continue;
            }

            // Local ledger vs saga mismatches that need a provider probe.
            if (localByLogical.TryGetValue(outcome.LogicalOperationId.Value, out var local))
            {
                if (local.Succeeded && outcome.Status == SagaItemOutcomeStatus.Failed)
                {
                    selected.Add(outcome);
                }
                else if (!local.Succeeded && outcome.Status == SagaItemOutcomeStatus.Succeeded)
                {
                    selected.Add(outcome);
                }
            }
        }

        return selected;
    }

    private Task SyncRequestStatusAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (saga.State is not (
            SagaState.Completed or
            SagaState.Failed or
            SagaState.PartiallyCompleted))
        {
            // Still reconciling — leave request as-is (may already be PartiallyFulfilled).
            return Task.CompletedTask;
        }

        if (request.Status is not (
            LicenseExtensionRequestStatus.InFulfillment or
            LicenseExtensionRequestStatus.PartiallyFulfilled))
        {
            return Task.CompletedTask;
        }

        try
        {
            request.CompleteFulfillment(saga.SucceededItemCount, saga.FailedItemCount);
        }
        catch (Domain.Common.DomainException ex)
        {
            _logger.LogDebug(ex, "Request status already settled for {RequestId}", request.Id.Value);
        }

        return Task.CompletedTask;
    }

    private Task AppendAuditAsync(
        string eventType,
        RequestId requestId,
        LogicalOperationId? logicalOperationId,
        string actor,
        string detail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        _audit.AppendAsync(
            new AuditEventRecord(
                Guid.NewGuid(),
                eventType,
                requestId,
                logicalOperationId,
                actor,
                detail,
                correlationId,
                DateTimeOffset.UtcNow),
            cancellationToken);
}
