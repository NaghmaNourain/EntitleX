using Amazon.SQS;
using Amazon.SQS.Model;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Messaging.Sqs;

/// <summary>
/// Best-effort SQS depth hook via GetQueueAttributes (ApproximateNumberOfMessages).
/// Does not label by message id — queue name only when logging.
/// </summary>
public sealed class SqsDepthProbe
{
    private readonly SqsMessagePublisher _publisher;
    private readonly SqsOptions _options;
    private readonly ILogger<SqsDepthProbe> _logger;

    public SqsDepthProbe(
        SqsMessagePublisher publisher,
        IOptions<MessagingOptions> options,
        ILogger<SqsDepthProbe> logger)
    {
        _publisher = publisher;
        _options = options.Value.Sqs;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var queueUrl = await _publisher.ResolveQueueUrlAsync(
                _options.FulfillmentQueueName,
                cancellationToken);

            var response = await _publisher.Client.GetQueueAttributesAsync(
                new GetQueueAttributesRequest
                {
                    QueueUrl = queueUrl,
                    AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"]
                },
                cancellationToken);

            var visible = Parse(response.Attributes, "ApproximateNumberOfMessages");
            var notVisible = Parse(response.Attributes, "ApproximateNumberOfMessagesNotVisible");
            EntitleXMeters.SqsApproximateDepth = visible + notVisible;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "SQS depth probe failed (hook remains at last value)");
        }
    }

    private static long Parse(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var raw) && long.TryParse(raw, out var value) ? value : 0;
}
