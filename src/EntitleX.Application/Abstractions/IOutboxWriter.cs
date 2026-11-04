using EntitleX.Messaging.Contracts;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Enqueues an integration message into the transactional outbox (same unit of work).
/// </summary>
public interface IOutboxWriter
{
    Task EnqueueAsync(
        MessageEnvelope envelope,
        OutboxDispatchTarget target,
        CancellationToken cancellationToken = default);
}

public sealed record OutboxDispatchTarget(
    OutboxTransport Transport,
    string Destination,
    string? PartitionKey = null);

public enum OutboxTransport
{
    Sqs = 0,
    Kafka = 1
}
