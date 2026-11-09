using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.LicenseExtension.Retry;

/// <summary>
/// Lightweight gate used by unit tests. Production registers
/// <see cref="ReconciliationGate"/> which also checks the local fulfillment ledger.
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
