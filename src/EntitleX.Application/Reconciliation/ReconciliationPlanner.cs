using EntitleX.Application.Abstractions;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Reconciliation;

/// <summary>
/// Probes provider status for a candidate outcome and plans the apply action.
/// </summary>
public sealed class ReconciliationPlanner
{
    private readonly IReadOnlyDictionary<ProviderSystem, IProviderOperationStatusClient> _providerClients;
    private readonly ReconciliationApplier _applier;
    private readonly ILogger<ReconciliationPlanner> _logger;

    public ReconciliationPlanner(
        IEnumerable<IProviderOperationStatusClient> providerClients,
        ReconciliationApplier applier,
        ILogger<ReconciliationPlanner> logger)
    {
        _providerClients = providerClients.ToDictionary(c => c.Provider);
        _applier = applier;
        _logger = logger;
    }

    public async Task<ReconciliationItemResult> ReconcileItemAsync(
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
            await _applier.AppendAuditAsync(
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
            await _applier.AppendAuditAsync(
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
                await _applier.ApplyConfirmedSuccessAsync(
                    request, saga, requestItem, outcome, status, actor, correlationId, cancellationToken),

            ProviderOperationLookupKind.ConfirmedFailed =>
                await _applier.ApplyConfirmedFailureAsync(
                    request, saga, requestItem, outcome, status, actor, correlationId, cancellationToken),

            ProviderOperationLookupKind.NotFound or ProviderOperationLookupKind.Unknown =>
                await _applier.KeepUnknownAsync(
                    request, saga, outcome, status, actor, correlationId, cancellationToken),

            _ => await _applier.KeepUnknownAsync(
                request, saga, outcome, status, actor, correlationId, cancellationToken)
        };
    }
}
