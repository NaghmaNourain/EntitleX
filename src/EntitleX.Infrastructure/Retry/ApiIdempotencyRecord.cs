using EntitleX.BuildingBlocks.Ids;

namespace EntitleX.Infrastructure.Retry;

/// <summary>
/// DB-backed API idempotency record for selective-retry (lost-response replay).
/// </summary>
public sealed class ApiIdempotencyRecord
{
    private ApiIdempotencyRecord()
    {
        IdempotencyKey = null!;
    }

    public ApiIdempotencyRecord(
        string idempotencyKey,
        RequestId sourceRequestId,
        RequestId retryRequestId,
        string? notes,
        DateTimeOffset createdAtUtc)
    {
        IdempotencyKey = idempotencyKey;
        SourceRequestId = sourceRequestId;
        RetryRequestId = retryRequestId;
        Notes = notes;
        CreatedAtUtc = createdAtUtc;
    }

    public string IdempotencyKey { get; private set; }

    public RequestId SourceRequestId { get; private set; }

    public RequestId RetryRequestId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
