using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Platform-local fulfillment ledger (fulfillment_item_results) used to detect
/// missing completions and local↔saga mismatches during reconciliation.
/// </summary>
public sealed record LocalFulfillmentOutcome(
    Guid RequestId,
    Guid RequestItemId,
    Guid LogicalOperationId,
    Guid AttemptId,
    string EntitlementId,
    string Provider,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset RecordedAtUtc);

public interface ILocalFulfillmentOutcomeStore
{
    /// <summary>
    /// Preferred outcome for a LogicalOperationId: Succeeded wins, else latest by RecordedAtUtc.
    /// </summary>
    Task<LocalFulfillmentOutcome?> FindByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalFulfillmentOutcome>> ListByRequestAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently records a local outcome (unique on LogicalOperationId + AttemptId).
    /// Returns false when a row already exists for that attempt.
    /// </summary>
    Task<bool> TryRecordAsync(
        LocalFulfillmentOutcome outcome,
        CancellationToken cancellationToken = default);
}
