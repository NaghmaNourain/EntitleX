using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.Fulfillment;

public sealed class FulfillmentOutboxService : IFulfillmentOutboxService
{
    private readonly IOutboxWriter _outboxWriter;
    private readonly MessagingOptions _options;

    public FulfillmentOutboxService(
        IOutboxWriter outboxWriter,
        IOptions<MessagingOptions> options)
    {
        _outboxWriter = outboxWriter;
        _options = options.Value;
    }

    public async Task EnqueueAfterFinalApprovalAsync(
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

        var messageId = MessageId.New();
        var correlation = correlationId ?? CorrelationId.New().Value;

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
            causationId: causationId,
            requestId: request.Id.Value);

        await _outboxWriter.EnqueueAsync(
            envelope,
            new OutboxDispatchTarget(
                OutboxTransport.Sqs,
                _options.Sqs.FulfillmentQueueName),
            cancellationToken);
    }
}
