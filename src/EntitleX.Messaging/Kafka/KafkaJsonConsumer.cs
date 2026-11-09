using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Metrics;
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

    /// <summary>W3C parent context extracted from Kafka headers (if present).</summary>
    public ActivityContext TraceContext { get; init; }
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
        var destination = "subscribed-topics";
        var sw = Stopwatch.StartNew();
        var success = false;
        Activity? activity = null;

        try
        {
            var result = _consumer.Consume(timeout);
            if (result?.Message?.Value is null)
            {
                success = true;
                return null;
            }

            destination = result.Topic;
            TraceContextPropagator.TryParseFromKafkaHeaders(result.Message.Headers, out var parent);
            activity = MessagingTelemetry.StartConsume("kafka", destination, parent);
            activity?.SetTag("messaging.destination", destination);

            RefreshLagHook(result);

            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(
                result.Message.Value,
                MessagingJson.Options)
                ?? throw new InvalidOperationException("Kafka payload deserialized to null envelope.");

            success = true;
            return new KafkaConsumedMessage
            {
                Envelope = envelope,
                Topic = result.Topic,
                Partition = result.Partition.Value,
                Offset = result.Offset.Value,
                TraceContext = parent
            };
        }
        catch (ConsumeException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Error.Reason);
            _logger.LogError(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
            throw;
        }
        finally
        {
            MessagingTelemetry.RecordOperation(
                "kafka",
                "consume",
                destination,
                sw.Elapsed.TotalSeconds,
                success);
            activity?.Dispose();
        }
    }

    private void RefreshLagHook(ConsumeResult<string, string> result)
    {
        try
        {
            var watermark = _consumer.GetWatermarkOffsets(result.TopicPartition);
            if (watermark.High.Value >= 0 && result.Offset.Value >= 0)
            {
                var lag = Math.Max(0, watermark.High.Value - (result.Offset.Value + 1));
                EntitleXMeters.KafkaConsumerLagHook = lag;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kafka lag hook refresh failed");
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
