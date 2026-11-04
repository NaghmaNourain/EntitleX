using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.LicenseExtension.Retry;

/// <summary>
/// Phase 7 gate: blocks retry when the saga reports uncertain outcomes or
/// <see cref="SagaState.ReconciliationRequired"/>. Full repair lives in Phase 8.
/// </summary>
public sealed class StubReconciliationGate : IReconciliationGate
{
    public Task<string?> GetRetryBlockReasonAsync(
        RequestId sourceRequestId,
        LicenseExtensionSaga saga,
        CancellationToken cancellationToken = default)
    {
        if (saga.State == SagaState.ReconciliationRequired)
        {
            return Task.FromResult<string?>(
                $"Request '{sourceRequestId.Value}' requires reconciliation before selective retry " +
                "(saga state ReconciliationRequired).");
        }

        var uncertain = saga.ItemOutcomes
            .Where(o => o.Status == SagaItemOutcomeStatus.Uncertain)
            .Select(o => o.LogicalOperationId.Value)
            .ToArray();

        if (uncertain.Length > 0)
        {
            return Task.FromResult<string?>(
                $"Request '{sourceRequestId.Value}' has uncertain item outcomes pending reconciliation: " +
                string.Join(", ", uncertain));
        }

        return Task.FromResult<string?>(null);
    }
}
