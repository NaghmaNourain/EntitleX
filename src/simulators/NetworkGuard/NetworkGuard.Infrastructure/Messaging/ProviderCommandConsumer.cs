using EntitleX.BuildingBlocks.Ids;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Kafka;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Application.Models;

namespace NetworkGuard.Infrastructure.Messaging;

/// <summary>
/// Consumes NetworkGuard license-extension commands from Kafka, applies them
/// idempotently, and publishes completion/failure events.
/// Post-apply transport errors rethrow (no Failed event) so Kafka retries Completed.
/// </summary>
public sealed class ProviderCommandConsumer(
    IServiceScopeFactory scopeFactory,
    IKafkaMessagePublisher kafka,
    IOptions<MessagingOptions> options,
    ILogger<ProviderCommandConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafkaOptions = options.Value.Kafka;
        logger.LogInformation(
            "NetworkGuard Kafka command consumer started on {Topic}",
            kafkaOptions.NetworkGuardCommandsTopic);

        using var consumer = new KafkaJsonConsumer(
            kafkaOptions.NetworkGuardConsumerGroup,
            [kafkaOptions.NetworkGuardCommandsTopic],
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

                await HandleAsync(message.Envelope, stoppingToken);
                consumer.Commit(message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "NetworkGuard Kafka consume loop failure");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        logger.LogInformation("NetworkGuard Kafka command consumer shutting down gracefully");
    }

    private async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.MessageType != MessageTypes.ProviderLicenseExtensionCommand)
        {
            logger.LogWarning("Ignoring message type {MessageType}", envelope.MessageType);
            return;
        }

        var command = envelope.DeserializePayload<ProviderLicenseExtensionCommandV1>();
        if (!command.Provider.Equals("NetworkGuard", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var applier = scope.ServiceProvider.GetRequiredService<ILicenseExtensionApplier>();

        LicenseOperationDto result;
        try
        {
            result = await applier.ApplyAsync(
                command.EntitlementId,
                new SubmitLicenseExtensionRequest(
                    command.LogicalOperationId,
                    command.AttemptId,
                    command.NewExpiryUtc),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "NetworkGuard failed applying logical operation {LogicalOperationId}",
                command.LogicalOperationId);

            var failed = new ProviderLicenseExtensionFailedV1(
                command.RequestId,
                command.RequestItemId,
                command.EntitlementId,
                "NetworkGuard",
                command.LogicalOperationId,
                command.AttemptId,
                "ApplyFailed",
                ex.Message);

            var failedEnvelope = MessageEnvelope.Create(
                MessageTypes.ProviderLicenseExtensionFailed,
                failed,
                MessageId.New().Value,
                correlationId: envelope.CorrelationId,
                causationId: envelope.MessageId,
                requestId: command.RequestId,
                logicalOperationId: command.LogicalOperationId);

            await kafka.PublishAsync(
                options.Value.Kafka.ProviderResultsTopic,
                partitionKey: command.EntitlementId,
                failedEnvelope,
                cancellationToken);
            return;
        }

        var completed = new ProviderLicenseExtensionCompletedV1(
            command.RequestId,
            command.RequestItemId,
            command.EntitlementId,
            "NetworkGuard",
            command.LogicalOperationId,
            command.AttemptId,
            result.PreviousExpiryUtc,
            result.NewExpiryUtc,
            result.AlreadyApplied);

        var resultEnvelope = MessageEnvelope.Create(
            MessageTypes.ProviderLicenseExtensionCompleted,
            completed,
            MessageId.New().Value,
            correlationId: envelope.CorrelationId,
            causationId: envelope.MessageId,
            requestId: command.RequestId,
            logicalOperationId: command.LogicalOperationId);

        await kafka.PublishAsync(
            options.Value.Kafka.ProviderResultsTopic,
            partitionKey: command.EntitlementId,
            resultEnvelope,
            cancellationToken);
    }
}
