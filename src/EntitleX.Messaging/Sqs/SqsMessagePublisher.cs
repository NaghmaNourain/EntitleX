using System.Diagnostics;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Metrics;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Messaging.Sqs;

public sealed class SqsMessagePublisher : ISqsMessagePublisher, IAsyncDisposable
{
    private readonly IAmazonSQS _client;
    private readonly SqsOptions _options;
    private readonly ILogger<SqsMessagePublisher> _logger;
    private readonly Dictionary<string, string> _queueUrls = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _resolveLock = new(1, 1);

    public SqsMessagePublisher(
        IOptions<MessagingOptions> options,
        ILogger<SqsMessagePublisher> logger,
        IHostEnvironment environment)
    {
        _options = options.Value.Sqs;
        _logger = logger;
        _client = CreateClient(_options, environment);
    }

    public static IAmazonSQS CreateClient(SqsOptions options, IHostEnvironment environment)
    {
        var config = new AmazonSQSConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region)
        };

        var hasCustomEndpoint = !string.IsNullOrWhiteSpace(options.ServiceUrl);
        if (hasCustomEndpoint)
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }

        var accessKey = options.AccessKey?.Trim() ?? string.Empty;
        var secretKey = options.SecretKey?.Trim() ?? string.Empty;
        var usesTestKeys = string.Equals(accessKey, "test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(secretKey, "test", StringComparison.OrdinalIgnoreCase);

        if (usesTestKeys && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Messaging:Sqs AccessKey/SecretKey must not be 'test' outside Development. " +
                "Leave ServiceUrl empty to use the default AWS credential chain (task role / instance profile).");
        }

        if (!hasCustomEndpoint)
        {
            // Real AWS: rely on the default credential chain (ECS task role, IRSA, etc.).
            return new AmazonSQSClient(config);
        }

        // LocalStack / custom endpoint: static keys are expected in Development.
        return new AmazonSQSClient(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    public async Task PublishAsync(
        string queueName,
        MessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        using var activity = MessagingTelemetry.StartProduce("sqs", queueName);
        var sw = Stopwatch.StartNew();
        var success = false;

        try
        {
            var queueUrl = await ResolveQueueUrlAsync(queueName, cancellationToken);
            var body = JsonSerializer.Serialize(envelope, MessagingJson.Options);

            // W3C trace context as SQS message attributes (low-cardinality keys only).
            var attributes = new Dictionary<string, MessageAttributeValue>
            {
                ["MessageType"] = new() { DataType = "String", StringValue = envelope.MessageType },
                ["SchemaVersion"] = new() { DataType = "String", StringValue = envelope.SchemaVersion },
                ["MessageId"] = new()
                {
                    DataType = "String",
                    StringValue = envelope.MessageId.ToString("D")
                }
            };

            if (Activity.Current is { } current)
            {
                attributes["traceparent"] = new()
                {
                    DataType = "String",
                    StringValue = current.Id ?? string.Empty
                };
                if (!string.IsNullOrEmpty(current.TraceStateString))
                {
                    attributes["tracestate"] = new()
                    {
                        DataType = "String",
                        StringValue = current.TraceStateString
                    };
                }
            }

            await _client.SendMessageAsync(
                new SendMessageRequest
                {
                    QueueUrl = queueUrl,
                    MessageBody = body,
                    MessageAttributes = attributes
                },
                cancellationToken);

            success = true;
            _logger.LogDebug(
                "Published SQS message {MessageId} type {MessageType} to {Queue}",
                envelope.MessageId,
                envelope.MessageType,
                queueName);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            MessagingTelemetry.RecordOperation("sqs", "publish", queueName, sw.Elapsed.TotalSeconds, success);
        }
    }

    public async Task<string> ResolveQueueUrlAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        if (_queueUrls.TryGetValue(queueName, out var cached))
        {
            return cached;
        }

        await _resolveLock.WaitAsync(cancellationToken);
        try
        {
            if (_queueUrls.TryGetValue(queueName, out cached))
            {
                return cached;
            }

            var response = await _client.GetQueueUrlAsync(queueName, cancellationToken);
            _queueUrls[queueName] = response.QueueUrl;
            return response.QueueUrl;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    public IAmazonSQS Client => _client;

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _resolveLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
