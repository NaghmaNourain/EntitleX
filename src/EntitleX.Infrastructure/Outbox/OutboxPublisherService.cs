using System.Text.Json;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Publishes claimed outbox rows to SQS or Kafka with retries and poison handling.
/// </summary>
public sealed class OutboxPublisherService
{
    private readonly EntitleXDbContext _db;
    private readonly OutboxClaimer _claimer;
    private readonly ISqsMessagePublisher _sqs;
    private readonly IKafkaMessagePublisher _kafka;
    private readonly IOutboxMetrics _metrics;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly string _workerId = $"outbox-{Environment.MachineName}-{Guid.NewGuid():N}"[..48];

    public OutboxPublisherService(
        EntitleXDbContext db,
        OutboxClaimer claimer,
        ISqsMessagePublisher sqs,
        IKafkaMessagePublisher kafka,
        IOutboxMetrics metrics,
        IOptions<MessagingOptions> options,
        ILogger<OutboxPublisherService> logger)
    {
        _db = db;
        _claimer = claimer;
        _sqs = sqs;
        _kafka = kafka;
        _metrics = metrics;
        _options = options.Value.Outbox;
        _logger = logger;
    }

    public async Task<int> PublishOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await _claimer.ClaimBatchAsync(_workerId, cancellationToken);
        _metrics.RecordClaimed(claimed.Count);

        foreach (var message in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PublishOneAsync(message, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task PublishOneAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = BuildEnvelope(message);

            if (message.DestinationKind == OutboxDestinationKind.Sqs)
            {
                await _sqs.PublishAsync(message.Destination, envelope, cancellationToken);
            }
            else
            {
                await _kafka.PublishAsync(
                    message.Destination,
                    message.PartitionKey,
                    envelope,
                    cancellationToken);
            }

            message.MarkPublished(DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            _metrics.RecordPublished(message.MessageType, message.DestinationKind.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to publish outbox message {OutboxId} type {MessageType} attempt {Attempt}",
                message.Id,
                message.MessageType,
                message.AttemptCount);

            _metrics.RecordPublishFailure(message.MessageType);

            if (message.AttemptCount >= message.MaxAttempts)
            {
                message.MarkPoison(ex.Message, DateTimeOffset.UtcNow);
                _metrics.RecordPoison(message.MessageType);
            }
            else
            {
                var delaySeconds = ComputeRetryDelaySeconds(message.AttemptCount);
                message.MarkRetry(ex.Message, DateTimeOffset.UtcNow.AddSeconds(delaySeconds));
                _metrics.RecordRetry(message.MessageType, message.AttemptCount);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private int ComputeRetryDelaySeconds(int attempt)
    {
        var exp = Math.Min(
            _options.MaxRetryDelaySeconds,
            _options.BaseRetryDelaySeconds * Math.Pow(2, Math.Max(0, attempt - 1)));
        return (int)Math.Clamp(exp, _options.BaseRetryDelaySeconds, _options.MaxRetryDelaySeconds);
    }

    private static MessageEnvelope BuildEnvelope(OutboxMessage message)
    {
        using var document = JsonDocument.Parse(message.PayloadJson);
        return new MessageEnvelope(
            message.SchemaVersion,
            message.MessageType,
            message.MessageId,
            message.CorrelationId,
            message.CausationId,
            message.RequestId,
            message.LogicalOperationId,
            message.CreatedAtUtc,
            document.RootElement.Clone());
    }
}
