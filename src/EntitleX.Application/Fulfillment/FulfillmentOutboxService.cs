using EntitleX.Application.Abstractions;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Messaging.Contracts;
using EntitleX.SharedKernel.Ids;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.Fulfillment;

public sealed class FulfillmentOutboxService : IFulfillmentOutboxService
{
    private readonly IOutboxWriter _outboxWriter;
    private readonly FulfillmentEnqueueOptions _options;

    public FulfillmentOutboxService(
        IOutboxWriter outboxWriter,
        IOptions<FulfillmentEnqueueOptions> options)
    {
        _outboxWriter = outboxWriter;
        _options = options.Value;
    }

    public async Task<bool> EnqueueAfterFinalApprovalAsync(
        LicenseExtensionRequest request,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default)
    {
        if (request.Status != LicenseExtensionRequestStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Request '{request.Id}' must be Approved before fulfillment enqueue (was '{request.Status}').");
        }

        request.BeginFulfillment();

        var correlation = correlationId ?? CorrelationId.New().Value;
        var causation = causationId ?? MessageId.New().Value;

        if (_options.PreferDirectKafka)
        {
            await EnqueueProviderCommandsAsync(request, correlation, causation, cancellationToken);
            return true;
        }

        await EnqueueLegacySqsCommandAsync(request, correlation, causation, cancellationToken);
        return false;
    }

    private async Task EnqueueProviderCommandsAsync(
        LicenseExtensionRequest request,
        Guid correlation,
        Guid causation,
        CancellationToken cancellationToken)
    {
        foreach (var item in request.Items)
        {
            var providerCommand = new ProviderLicenseExtensionCommandV1(
                request.Id.Value,
                item.Id.Value,
                item.EntitlementId.Value,
                item.Provider.ToString(),
                item.LogicalOperationId.Value,
                item.AttemptId.Value,
                item.RequestedExpiryUtc);

            var messageId = MessageId.New();
            var envelope = MessageEnvelope.Create(
                MessageTypes.ProviderLicenseExtensionCommand,
                providerCommand,
                messageId.Value,
                correlationId: correlation,
                causationId: causation,
                requestId: request.Id.Value,
                logicalOperationId: item.LogicalOperationId.Value);

            var topic = ResolveTopic(item.Provider.ToString());
            await _outboxWriter.EnqueueAsync(
                envelope,
                new OutboxDispatchTarget(
                    OutboxTransport.Kafka,
                    topic,
                    PartitionKey: item.EntitlementId.Value),
                cancellationToken);
        }
    }

    private async Task EnqueueLegacySqsCommandAsync(
        LicenseExtensionRequest request,
        Guid correlation,
        Guid causation,
        CancellationToken cancellationToken)
    {
        var messageId = MessageId.New();
        var command = new FulfillmentCommandV1(
            request.Id.Value,
            request.ExtensionNumber,
            request.Items.Select(i => new FulfillmentItemV1(
                i.Id.Value,
                i.EntitlementId.Value,
                i.Provider.ToString(),
                i.LogicalOperationId.Value,
                i.AttemptId.Value,
                i.CurrentExpiryUtc,
                i.RequestedExpiryUtc)).ToList());

        var envelope = MessageEnvelope.Create(
            MessageTypes.FulfillmentCommand,
            command,
            messageId.Value,
            correlationId: correlation,
            causationId: causation,
            requestId: request.Id.Value);

        await _outboxWriter.EnqueueAsync(
            envelope,
            new OutboxDispatchTarget(
                OutboxTransport.Sqs,
                _options.FulfillmentQueueName),
            cancellationToken);
    }

    private string ResolveTopic(string provider) =>
        provider.Equals("NetworkGuard", StringComparison.OrdinalIgnoreCase)
            ? _options.NetworkGuardCommandsTopic
            : _options.CloudShieldCommandsTopic;
}
