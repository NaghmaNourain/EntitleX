using EntitleX.SharedKernel.Ids;

namespace EntitleX.Infrastructure.Retry;

/// <summary>
/// Per-item lock preventing concurrent overlapping selective retries for the same
/// source <see cref="LogicalOperationId"/>.
/// </summary>
public sealed class RetryItemClaim
{
    private RetryItemClaim()
    {
    }

    public RetryItemClaim(
        RequestId sourceRequestId,
        LogicalOperationId logicalOperationId,
        RequestId retryRequestId,
        string idempotencyKey,
        DateTimeOffset claimedAtUtc)
    {
        SourceRequestId = sourceRequestId;
        LogicalOperationId = logicalOperationId;
        RetryRequestId = retryRequestId;
        IdempotencyKey = idempotencyKey;
        ClaimedAtUtc = claimedAtUtc;
    }

    public RequestId SourceRequestId { get; private set; }

    public LogicalOperationId LogicalOperationId { get; private set; }

    public RequestId RetryRequestId { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    public DateTimeOffset ClaimedAtUtc { get; private set; }
}
