using EntitleX.Infrastructure.Outbox;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Kafka;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;

namespace EntitleX.FulfillmentWorker;

/// <summary>Consumes provider completion/failure events from Kafka (idempotent).</summary>
public sealed class ProviderResultsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<ProviderResultsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Provider results Kafka worker started");
        var kafka = options.Value.Kafka;

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

                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ProviderResultProcessor>();
                await DispatchAsync(processor, message.Envelope, stoppingToken);
                consumer.Commit(message);
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
