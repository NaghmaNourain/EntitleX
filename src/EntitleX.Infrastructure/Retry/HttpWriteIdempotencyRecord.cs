namespace EntitleX.Infrastructure.Retry;

/// <summary>
/// DB-backed HTTP write idempotency (create / approval start / decide).
/// Distinct from <see cref="ApiIdempotencyRecord"/> which is retry-specific.
/// </summary>
public sealed class HttpWriteIdempotencyRecord
{
    private HttpWriteIdempotencyRecord()
    {
        IdempotencyKey = null!;
        Operation = null!;
        ActorUserId = null!;
        RequestFingerprint = null!;
        ResourceId = null!;
    }

    public HttpWriteIdempotencyRecord(
        string idempotencyKey,
        string operation,
        string actorUserId,
        string requestFingerprint,
        string resourceId,
        DateTimeOffset createdAtUtc)
    {
        IdempotencyKey = idempotencyKey;
        Operation = operation;
        ActorUserId = actorUserId;
        RequestFingerprint = requestFingerprint;
        ResourceId = resourceId;
        CreatedAtUtc = createdAtUtc;
    }

    public string IdempotencyKey { get; private set; }

    public string Operation { get; private set; }

    public string ActorUserId { get; private set; }

    public string RequestFingerprint { get; private set; }

    public string ResourceId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
