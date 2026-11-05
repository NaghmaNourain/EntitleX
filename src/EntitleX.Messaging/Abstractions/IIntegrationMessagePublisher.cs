using EntitleX.Messaging.Contracts;

namespace EntitleX.Messaging.Abstractions;

public interface ISqsMessagePublisher
{
    Task PublishAsync(
        string queueName,
        MessageEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public interface IKafkaMessagePublisher
{
    Task PublishAsync(
        string topic,
        string? partitionKey,
        MessageEnvelope envelope,
        CancellationToken cancellationToken = default);
}
