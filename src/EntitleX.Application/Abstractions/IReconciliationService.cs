using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.Abstractions;

public enum ReconciliationItemAction
{
    None = 0,
    RepairedSuccess = 1,
    MarkedFailedRetryEligible = 2,
    KeptUnknown = 3,
    MismatchNoted = 4
}

public sealed record ReconciliationItemResult(
    Guid LogicalOperationId,
    string Provider,
    ReconciliationItemAction Action,
    string Detail);

public sealed record ReconciliationRunResult(
    Guid RequestId,
    SagaState SagaState,
    IReadOnlyList<ReconciliationItemResult> Items,
    bool StillRequiresReconciliation);

public interface IReconciliationService
{
    /// <summary>
    /// Scans elapsed timeouts, then repairs candidates (uncertain / missing /
    /// mismatched) without blind-retrying unknown external effects.
    /// </summary>
    Task<IReadOnlyList<ReconciliationRunResult>> RunBatchAsync(
        int take = 50,
        string actor = "reconciliation-worker",
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reconcile a single request (manual admin or targeted worker pass).</summary>
    Task<ReconciliationRunResult> ReconcileRequestAsync(
        RequestId requestId,
        string actor,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
