using EntitleX.Application.Abstractions;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.SharedKernel.Ids;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Reconciliation;

/// <summary>
/// Finds uncertain / missing / mismatched outcomes, probes providers by
/// LogicalOperationId, and repairs local saga state. Never blind-retries
/// unknown external effects. Ages long-lived Uncertain items to Failed.
/// Orchestrates detect → plan/probe → apply collaborators.
/// </summary>
public sealed class ReconciliationService : IReconciliationService
{
    private readonly ILicenseExtensionSagaRepository _sagaRepository;
    private readonly ILicenseExtensionRequestRepository _requestRepository;
    private readonly ILicenseExtensionSagaOrchestrator _sagaOrchestrator;
    private readonly ILocalFulfillmentOutcomeStore _localOutcomes;
    private readonly IAuditEventStore _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ReconciliationDetector _detector;
    private readonly ReconciliationPlanner _planner;
    private readonly ReconciliationApplier _applier;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        ILicenseExtensionSagaRepository sagaRepository,
        ILicenseExtensionRequestRepository requestRepository,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        ILocalFulfillmentOutcomeStore localOutcomes,
        IAuditEventStore audit,
        IUnitOfWork unitOfWork,
        ReconciliationDetector detector,
        ReconciliationPlanner planner,
        ReconciliationApplier applier,
        ILogger<ReconciliationService> logger)
    {
        _sagaRepository = sagaRepository;
        _requestRepository = requestRepository;
        _sagaOrchestrator = sagaOrchestrator;
        _localOutcomes = localOutcomes;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _detector = detector;
        _planner = planner;
        _applier = applier;
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

        var candidates = _detector.SelectCandidateOutcomes(
            saga,
            await _localOutcomes.ListByRequestAsync(requestId, cancellationToken));

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

            itemResults.Add(await _planner.ReconcileItemAsync(
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

        await _applier.SyncRequestStatusAsync(request, saga, cancellationToken);
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

        _logger.LogDebug(
            "Reconciliation pass for {RequestId} finished with {ItemCount} item result(s)",
            requestId.Value,
            itemResults.Count);

        return new ReconciliationRunResult(
            requestId.Value,
            saga.State,
            itemResults,
            stillRequired);
    }
}
