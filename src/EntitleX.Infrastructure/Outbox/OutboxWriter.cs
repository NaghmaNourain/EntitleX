using System.Text.Json;
using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Outbox;

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly EntitleXDbContext _db;
    private readonly OutboxOptions _options;

    public OutboxWriter(EntitleXDbContext db, IOptions<MessagingOptions> options)
    {
        _db = db;
        _options = options.Value.Outbox;
    }

    public Task EnqueueAsync(
        MessageEnvelope envelope,
        OutboxDispatchTarget target,
        CancellationToken cancellationToken = default)
    {
        var payloadJson = envelope.Payload.GetRawText();
        var destinationKind = target.Transport switch
        {
            OutboxTransport.Sqs => OutboxDestinationKind.Sqs,
            OutboxTransport.Kafka => OutboxDestinationKind.Kafka,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Transport, null)
        };

        var message = OutboxMessage.Create(
            envelope.MessageId,
            envelope.MessageType,
            payloadJson,
            destinationKind,
            target.Destination,
            target.PartitionKey,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.RequestId,
            envelope.LogicalOperationId,
            envelope.SchemaVersion,
            _options.MaxAttempts,
            envelope.OccurredAtUtc);

        _db.OutboxMessages.Add(message);
        return Task.CompletedTask;
    }
}
