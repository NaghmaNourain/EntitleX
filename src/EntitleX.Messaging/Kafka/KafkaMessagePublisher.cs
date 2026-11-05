using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Metrics;
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
        using var activity = MessagingTelemetry.StartProduce("kafka", topic);
        var sw = Stopwatch.StartNew();
        var success = false;

        try
        {
            var body = JsonSerializer.Serialize(envelope, MessagingJson.Options);
            var headers = new Headers
            {
                { "message-type", Encoding.UTF8.GetBytes(envelope.MessageType) },
                { "schema-version", Encoding.UTF8.GetBytes(envelope.SchemaVersion) },
                { "message-id", Encoding.UTF8.GetBytes(envelope.MessageId.ToString("D")) }
            };

            if (Activity.Current is { } current && current.Id is not null)
            {
                headers.Add("traceparent", Encoding.UTF8.GetBytes(current.Id));
                if (!string.IsNullOrEmpty(current.TraceStateString))
                {
                    headers.Add("tracestate", Encoding.UTF8.GetBytes(current.TraceStateString));
                }
            }

            var message = new Message<string, string>
            {
                Key = partitionKey ?? envelope.MessageId.ToString("D"),
                Value = body,
                Headers = headers
            };

            var result = await _producer.ProduceAsync(topic, message, cancellationToken);
            success = true;
            _logger.LogDebug(
                "Published Kafka message {MessageId} type {MessageType} to {Topic} partition {Partition} offset {Offset}",
                envelope.MessageId,
                envelope.MessageType,
                topic,
                result.Partition.Value,
                result.Offset.Value);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            MessagingTelemetry.RecordOperation("kafka", "publish", topic, sw.Elapsed.TotalSeconds, success);
        }
    }

    public void Dispose() => _producer.Dispose();
}
