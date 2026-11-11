using EntitleX.Application.Abstractions;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.Reconciliation;

/// <summary>
/// Detects saga item outcomes that need a reconciliation probe
/// (uncertain/pending or local-ledger mismatches).
/// </summary>
public sealed class ReconciliationDetector
{
    public IReadOnlyList<SagaItemOutcome> SelectCandidateOutcomes(
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
}
