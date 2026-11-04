using EntitleX.BuildingBlocks.Ids;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Platform-local fulfillment ledger (fulfillment_item_results) used to detect
/// missing completions and local↔saga mismatches during reconciliation.
/// </summary>
public sealed record LocalFulfillmentOutcome(
    Guid RequestId,
    Guid RequestItemId,
    Guid LogicalOperationId,
    string EntitlementId,
    string Provider,
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset RecordedAtUtc);

public interface ILocalFulfillmentOutcomeStore
{
    Task<LocalFulfillmentOutcome?> FindByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalFulfillmentOutcome>> ListByRequestAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently records a corrective local outcome (unique on LogicalOperationId).
    /// Returns false when a row already exists.
    /// </summary>
    Task<bool> TryRecordAsync(
        LocalFulfillmentOutcome outcome,
        CancellationToken cancellationToken = default);
}
