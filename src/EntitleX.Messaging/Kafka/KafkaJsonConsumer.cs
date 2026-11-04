using System.Text.Json;
using Confluent.Kafka;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Messaging.Kafka;

public sealed class KafkaConsumedMessage
{
    public required MessageEnvelope Envelope { get; init; }

    public required string Topic { get; init; }

    public required int Partition { get; init; }

    public required long Offset { get; init; }
}

/// <summary>Thin Kafka consumer wrapper with graceful close.</summary>
public sealed class KafkaJsonConsumer : IDisposable
{
    private readonly IConsumer<string, string> _consumer;
    private readonly ILogger _logger;

    public KafkaJsonConsumer(
        string groupId,
        IEnumerable<string> topics,
        IOptions<MessagingOptions> options,
        ILogger logger)
    {
        _logger = logger;
        var kafka = options.Value.Kafka;

        _consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = groupId,
            ClientId = $"{kafka.ClientId}-{groupId}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = false
        }).Build();

        _consumer.Subscribe(topics);
    }

    public KafkaConsumedMessage? Consume(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = _consumer.Consume(timeout);
            if (result?.Message?.Value is null)
            {
                return null;
            }

            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(
                result.Message.Value,
                MessagingJson.Options)
                ?? throw new InvalidOperationException("Kafka payload deserialized to null envelope.");

            return new KafkaConsumedMessage
            {
                Envelope = envelope,
                Topic = result.Topic,
                Partition = result.Partition.Value,
                Offset = result.Offset.Value
            };
        }
        catch (ConsumeException ex)
        {
            _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
            throw;
        }
    }

    public void Commit(KafkaConsumedMessage message)
    {
        _consumer.Commit(
        [
            new TopicPartitionOffset(
                new TopicPartition(message.Topic, new Partition(message.Partition)),
                new Offset(message.Offset + 1))
        ]);
    }

    public void Dispose()
    {
        try
        {
            _consumer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kafka consumer close raised an exception during shutdown");
        }

        _consumer.Dispose();
    }
}
