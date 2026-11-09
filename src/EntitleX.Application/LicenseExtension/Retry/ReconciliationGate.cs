using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.LicenseExtension.Retry;

/// <summary>
/// Phase 8 gate: blocks selective retry while reconciliation is still required.
/// Uses saga outcomes plus the local fulfillment ledger for mismatch detection
/// (no outbound provider HTTP on the retry hot path).
/// </summary>
public sealed class ReconciliationGate : IReconciliationGate
{
    private readonly ILocalFulfillmentOutcomeStore _localOutcomes;

    public ReconciliationGate(ILocalFulfillmentOutcomeStore localOutcomes)
    {
        _localOutcomes = localOutcomes;
    }

    public async Task<string?> GetRetryBlockReasonAsync(
        RequestId sourceRequestId,
        LicenseExtensionSaga saga,
        CancellationToken cancellationToken = default)
    {
        if (saga.State == SagaState.ReconciliationRequired)
        {
            return $"Request '{sourceRequestId.Value}' requires reconciliation before selective retry " +
                   "(saga state ReconciliationRequired).";
        }

        var uncertain = saga.ItemOutcomes
            .Where(o => o.Status == SagaItemOutcomeStatus.Uncertain)
            .Select(o => o.LogicalOperationId.Value)
            .ToArray();

        if (uncertain.Length > 0)
        {
            return $"Request '{sourceRequestId.Value}' has uncertain item outcomes pending reconciliation: " +
                   string.Join(", ", uncertain);
        }

        // Real check: local ledger success while saga still Failed (or pending completion
        // recorded locally but saga never advanced) — must reconcile before retry.
        var local = await _localOutcomes.ListByRequestAsync(sourceRequestId, cancellationToken);
        if (local.Count == 0)
        {
            return null;
        }

        // Prefer Succeeded attempt when multiple ledger rows share a LogicalOperationId.
        var localByLogical = local
            .GroupBy(o => o.LogicalOperationId)
            .ToDictionary(
                g => g.Key,
                g => g.FirstOrDefault(o => o.Succeeded)
                     ?? g.OrderByDescending(o => o.RecordedAtUtc).First());
        var mismatches = new List<Guid>();

        foreach (var outcome in saga.ItemOutcomes)
        {
            if (!localByLogical.TryGetValue(outcome.LogicalOperationId.Value, out var row))
            {
                continue;
            }

            if (row.Succeeded && outcome.Status == SagaItemOutcomeStatus.Failed)
            {
                mismatches.Add(outcome.LogicalOperationId.Value);
            }
            else if (row.Succeeded && outcome.Status == SagaItemOutcomeStatus.Pending)
            {
                mismatches.Add(outcome.LogicalOperationId.Value);
            }
        }

        if (mismatches.Count > 0)
        {
            return $"Request '{sourceRequestId.Value}' has local↔saga fulfillment mismatches pending reconciliation: " +
                   string.Join(", ", mismatches);
        }

        return null;
    }
}
