namespace EntitleX.Application.Abstractions;

/// <summary>
/// HTTP write idempotency for create / approval start / decide (lost-response replay).
/// Selective retry continues to use <see cref="IRetryClaimStore"/>.
/// </summary>
public interface IApiIdempotencyStore
{
    Task<ApiIdempotencyLookup?> FindAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a completed write. Returns false when the key is already bound
    /// (caller should re-read and reconcile fingerprint).
    /// </summary>
    Task<bool> TryRecordAsync(
        string idempotencyKey,
        string operation,
        string actorUserId,
        string requestFingerprint,
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed record ApiIdempotencyLookup(
    string IdempotencyKey,
    string Operation,
    string ActorUserId,
    string RequestFingerprint,
    string ResourceId,
    DateTimeOffset CreatedAtUtc);

public static class ApiIdempotencyOperations
{
    public const string CreateLicenseExtensionRequest = "license-extension.create";
    public const string StartApproval = "approval.start";
    public const string DecideApproval = "approval.decide";
}
