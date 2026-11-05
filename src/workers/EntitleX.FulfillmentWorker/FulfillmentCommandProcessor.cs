using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;

namespace EntitleX.FulfillmentWorker;

/// <summary>
/// Turns a fulfillment command into per-item Kafka provider commands
/// (partition key = entitlement id) and advances the saga to Processing.
/// </summary>
public sealed class FulfillmentCommandProcessor(
    IKafkaMessagePublisher kafka,
    IConsumerIdempotencyStore idempotency,
    ILicenseExtensionSagaOrchestrator sagaOrchestrator,
    IUnitOfWork unitOfWork,
    IOptions<MessagingOptions> options,
    ILogger<FulfillmentCommandProcessor> logger)
{
    public const string ConsumerName = "entitlex-fulfillment-sqs";

    public async Task ProcessAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.MessageType, MessageTypes.FulfillmentCommand, StringComparison.Ordinal))
        {
            logger.LogWarning("Ignoring unexpected SQS message type {MessageType}", envelope.MessageType);
            return;
        }

        var claimed = await idempotency.TryBeginAsync(
            ConsumerName,
            envelope.MessageId.ToString("D"),
            envelope.MessageType,
            cancellationToken);

        if (!claimed)
        {
            logger.LogInformation(
                "Skipping duplicate fulfillment command {MessageId}",
                envelope.MessageId);
            return;
        }

        var command = envelope.DeserializePayload<FulfillmentCommandV1>();
        var kafkaOptions = options.Value.Kafka;

        foreach (var item in command.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var providerCommand = new ProviderLicenseExtensionCommandV1(
                command.RequestId,
                item.RequestItemId,
                item.EntitlementId,
                item.Provider,
                item.LogicalOperationId,
                item.AttemptId,
                item.RequestedExpiryUtc);

            var childMessageId = MessageId.New();
            var child = MessageEnvelope.Create(
                MessageTypes.ProviderLicenseExtensionCommand,
                providerCommand,
                childMessageId.Value,
                correlationId: envelope.CorrelationId,
                causationId: envelope.MessageId,
                requestId: command.RequestId,
                logicalOperationId: item.LogicalOperationId);

            var topic = ResolveTopic(item.Provider, kafkaOptions);
            await kafka.PublishAsync(
                topic,
                partitionKey: item.EntitlementId,
                child,
                cancellationToken);

            logger.LogInformation(
                "Published provider command for entitlement {EntitlementId} on {Topic} (logicalOp={LogicalOperationId})",
                item.EntitlementId,
                topic,
                item.LogicalOperationId);
        }

        await sagaOrchestrator.OnFulfillmentDispatchedAsync(
            RequestId.From(command.RequestId),
            envelope.CorrelationId?.ToString("D"),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string ResolveTopic(string provider, KafkaOptions kafka) =>
        provider.Equals("NetworkGuard", StringComparison.OrdinalIgnoreCase)
            ? kafka.NetworkGuardCommandsTopic
            : kafka.CloudShieldCommandsTopic;
}
