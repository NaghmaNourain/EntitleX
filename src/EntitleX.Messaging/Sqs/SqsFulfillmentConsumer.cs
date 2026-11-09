using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Metrics;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Messaging.Sqs;

public sealed class SqsReceivedMessage
{
    public required MessageEnvelope Envelope { get; init; }

    public required string ReceiptHandle { get; init; }

    public required string QueueUrl { get; init; }

    public int ApproximateReceiveCount { get; init; }

    /// <summary>W3C parent context extracted from SQS message attributes (if present).</summary>
    public ActivityContext TraceContext { get; init; }
}

/// <summary>Long-polls the fulfillment SQS queue with visibility-timeout handling.</summary>
public sealed class SqsFulfillmentConsumer
{
    private readonly SqsMessagePublisher _publisher;
    private readonly SqsOptions _options;
    private readonly ILogger<SqsFulfillmentConsumer> _logger;

    public SqsFulfillmentConsumer(
        SqsMessagePublisher publisher,
        IOptions<MessagingOptions> options,
        ILogger<SqsFulfillmentConsumer> logger)
    {
        _publisher = publisher;
        _options = options.Value.Sqs;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SqsReceivedMessage>> ReceiveAsync(
        CancellationToken cancellationToken = default)
    {
        var destination = _options.FulfillmentQueueName;
        var sw = Stopwatch.StartNew();
        var success = false;
        Activity? activity = null;

        try
        {
            var queueUrl = await _publisher.ResolveQueueUrlAsync(destination, cancellationToken);

            var response = await _publisher.Client.ReceiveMessageAsync(
                new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = Math.Clamp(_options.MaxNumberOfMessages, 1, 10),
                    WaitTimeSeconds = Math.Clamp(_options.WaitTimeSeconds, 0, 20),
                    VisibilityTimeout = Math.Clamp(_options.VisibilityTimeoutSeconds, 1, 43200),
                    MessageAttributeNames = ["All"],
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"]
                },
                cancellationToken);

            if (response.Messages is null || response.Messages.Count == 0)
            {
                success = true;
                return [];
            }

            var parent = default(ActivityContext);
            foreach (var candidate in response.Messages)
            {
                if (TraceContextPropagator.TryParseFromSqsMessageAttributes(
                        candidate.MessageAttributes,
                        out parent))
                {
                    break;
                }
            }

            activity = MessagingTelemetry.StartConsume("sqs", destination, parent);

            var results = new List<SqsReceivedMessage>(response.Messages.Count);
            foreach (var message in response.Messages)
            {
                try
                {
                    var envelope = JsonSerializer.Deserialize<MessageEnvelope>(
                        message.Body,
                        MessagingJson.Options)
                        ?? throw new InvalidOperationException("SQS body deserialized to null envelope.");

                    var receiveCount = 1;
                    if (message.Attributes.TryGetValue("ApproximateReceiveCount", out var raw)
                        && int.TryParse(raw, out var parsed))
                    {
                        receiveCount = parsed;
                    }

                    TraceContextPropagator.TryParseFromSqsMessageAttributes(
                        message.MessageAttributes,
                        out var messageParent);

                    results.Add(new SqsReceivedMessage
                    {
                        Envelope = envelope,
                        ReceiptHandle = message.ReceiptHandle,
                        QueueUrl = queueUrl,
                        ApproximateReceiveCount = receiveCount,
                        TraceContext = messageParent
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize SQS message; leaving for retry/DLQ");
                }
            }

            success = true;
            return results;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            MessagingTelemetry.RecordOperation("sqs", "receive", destination, sw.Elapsed.TotalSeconds, success);
            activity?.Dispose();
        }
    }

    public Task DeleteAsync(SqsReceivedMessage message, CancellationToken cancellationToken = default) =>
        _publisher.Client.DeleteMessageAsync(
            message.QueueUrl,
            message.ReceiptHandle,
            cancellationToken);

    /// <summary>Heartbeat: extend visibility so large fan-outs do not redeliver mid-process.</summary>
    public Task ExtendVisibilityAsync(
        SqsReceivedMessage message,
        int visibilityTimeoutSeconds,
        CancellationToken cancellationToken = default) =>
        _publisher.Client.ChangeMessageVisibilityAsync(
            message.QueueUrl,
            message.ReceiptHandle,
            Math.Clamp(visibilityTimeoutSeconds, 1, 43200),
            cancellationToken);

    public async Task MoveToDlqAndDeleteAsync(
        SqsReceivedMessage message,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dlqUrl = await _publisher.ResolveQueueUrlAsync(
                _options.FulfillmentDlqName,
                cancellationToken);

            await _publisher.Client.SendMessageAsync(
                new SendMessageRequest
                {
                    QueueUrl = dlqUrl,
                    MessageBody = JsonSerializer.Serialize(message.Envelope, MessagingJson.Options),
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["PoisonReason"] = new() { DataType = "String", StringValue = reason },
                        ["OriginalMessageId"] = new()
                        {
                            DataType = "String",
                            StringValue = message.Envelope.MessageId.ToString("D")
                        }
                    }
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to move message {MessageId} to DLQ; message will retry via visibility timeout",
                message.Envelope.MessageId);
            return;
        }

        await DeleteAsync(message, cancellationToken);
        _logger.LogError(
            "Moved poison SQS message {MessageId} to DLQ: {Reason}",
            message.Envelope.MessageId,
            reason);
    }
}
