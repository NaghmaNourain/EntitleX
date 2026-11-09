using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// DB-backed selective-retry claims: idempotency keys plus per-item locks that
/// prevent concurrent overlapping retries for the same source items.
/// Claims are taken inside a short Postgres transaction (never held across Kafka/SQS/HTTP).
/// </summary>
public interface IRetryClaimStore
{
    Task<RetryIdempotencyLookup?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims source items for a new retry. Returns false when any
    /// logical operation is already claimed by an overlapping retry.
    /// </summary>
    Task<bool> TryClaimItemsAsync(
        RequestId sourceRequestId,
        RequestId retryRequestId,
        string idempotencyKey,
        IReadOnlyList<LogicalOperationId> logicalOperationIds,
        string? notes,
        CancellationToken cancellationToken = default);
}

public sealed record RetryIdempotencyLookup(
    string IdempotencyKey,
    RequestId SourceRequestId,
    RequestId RetryRequestId,
    DateTimeOffset CreatedAtUtc);
