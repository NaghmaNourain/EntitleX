using System.Collections.Concurrent;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Kafka;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;

namespace EntitleX.ResultsWorker;

/// <summary>
/// Consumes provider completion/failure events from Kafka (idempotent).
/// After N consecutive handle failures for the same offset, publishes to the DLQ
/// topic and commits so the poison message does not block the partition forever.
/// </summary>
public sealed class ProviderResultsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    IKafkaMessagePublisher kafkaPublisher,
    ILogger<ProviderResultsWorker> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, int> _failureCounts = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Provider results Kafka worker started");
        var kafka = options.Value.Kafka;
        var maxAttempts = Math.Clamp(kafka.ProviderResultsMaxProcessAttempts, 1, 50);

        using var consumer = new KafkaJsonConsumer(
            kafka.ProviderResultsConsumerGroup,
            [kafka.ProviderResultsTopic],
            options,
            logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = consumer.Consume(TimeSpan.FromSeconds(1), stoppingToken);
                if (message is null)
                {
                    continue;
                }

                var offsetKey = $"{message.Topic}:{message.Partition}:{message.Offset}";
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ProviderResultProcessor>();
                    await DispatchAsync(processor, message.Envelope, stoppingToken);
                    _failureCounts.TryRemove(offsetKey, out _);
                    consumer.Commit(message);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var attempts = _failureCounts.AddOrUpdate(offsetKey, 1, static (_, n) => n + 1);
                    logger.LogError(
                        ex,
                        "Provider results handle failure for {OffsetKey} attempt {Attempt}/{Max}",
                        offsetKey,
                        attempts,
                        maxAttempts);

                    if (attempts >= maxAttempts)
                    {
                        await kafkaPublisher.PublishAsync(
                            kafka.ProviderResultsDlqTopic,
                            message.Envelope.LogicalOperationId?.ToString("D"),
                            message.Envelope,
                            stoppingToken);
                        _failureCounts.TryRemove(offsetKey, out _);
                        consumer.Commit(message);
                        logger.LogWarning(
                            "Published poison provider result {MessageId} to DLQ {Topic} and committed offset",
                            message.Envelope.MessageId,
                            kafka.ProviderResultsDlqTopic);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Provider results consume loop failure");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        logger.LogInformation("Provider results Kafka worker shutting down gracefully");
    }

    private static Task DispatchAsync(
        ProviderResultProcessor processor,
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.MessageType == MessageTypes.ProviderLicenseExtensionCompleted)
        {
            var payload = envelope.DeserializePayload<ProviderLicenseExtensionCompletedV1>();
            return processor.ProcessCompletedAsync(envelope, payload, cancellationToken);
        }

        if (envelope.MessageType == MessageTypes.ProviderLicenseExtensionFailed)
        {
            var payload = envelope.DeserializePayload<ProviderLicenseExtensionFailedV1>();
            return processor.ProcessFailedAsync(envelope, payload, cancellationToken);
        }

        return Task.CompletedTask;
    }
}
