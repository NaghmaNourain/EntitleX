using System.Text.Json;
using Confluent.Kafka;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Messaging.Kafka;

public sealed class KafkaMessagePublisher : IKafkaMessagePublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaMessagePublisher> _logger;

    public KafkaMessagePublisher(IOptions<MessagingOptions> options, ILogger<KafkaMessagePublisher> logger)
    {
        _logger = logger;
        var kafka = options.Value.Kafka;

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            ClientId = $"{kafka.ClientId}-producer",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 5
        }).Build();
    }

    public async Task PublishAsync(
        string topic,
        string? partitionKey,
        MessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(envelope, MessagingJson.Options);
        var message = new Message<string, string>
        {
            Key = partitionKey ?? envelope.MessageId.ToString("D"),
            Value = body,
            Headers =
            [
                new Header("message-type", System.Text.Encoding.UTF8.GetBytes(envelope.MessageType)),
                new Header("schema-version", System.Text.Encoding.UTF8.GetBytes(envelope.SchemaVersion)),
                new Header("message-id", System.Text.Encoding.UTF8.GetBytes(envelope.MessageId.ToString("D")))
            ]
        };

        var result = await _producer.ProduceAsync(topic, message, cancellationToken);
        _logger.LogDebug(
            "Published Kafka message {MessageId} type {MessageType} to {Topic} partition {Partition} offset {Offset}",
            envelope.MessageId,
            envelope.MessageType,
            topic,
            result.Partition.Value,
            result.Offset.Value);
    }

    public void Dispose() => _producer.Dispose();
}
