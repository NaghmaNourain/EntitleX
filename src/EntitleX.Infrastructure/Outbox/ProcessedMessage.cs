namespace EntitleX.Infrastructure.Outbox;

/// <summary>Consumer idempotency claim for at-least-once delivery.</summary>
public sealed class ProcessedMessage
{
    private ProcessedMessage()
    {
        ConsumerName = null!;
        IdempotencyKey = null!;
    }

    public ProcessedMessage(
        string consumerName,
        string idempotencyKey,
        DateTimeOffset processedAtUtc,
        string? messageType = null)
    {
        ConsumerName = consumerName;
        IdempotencyKey = idempotencyKey;
        ProcessedAtUtc = processedAtUtc;
        MessageType = messageType;
    }

    public string ConsumerName { get; private set; }

    public string IdempotencyKey { get; private set; }

    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public string? MessageType { get; private set; }
}
