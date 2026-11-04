namespace EntitleX.Messaging.Abstractions;

/// <summary>At-least-once consumer deduplication store.</summary>
public interface IConsumerIdempotencyStore
{
    Task<bool> TryBeginAsync(
        string consumerName,
        string idempotencyKey,
        string? messageType = null,
        CancellationToken cancellationToken = default);
}
