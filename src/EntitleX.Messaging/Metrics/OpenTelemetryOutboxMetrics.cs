using EntitleX.BuildingBlocks.Observability;
using EntitleX.Messaging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EntitleX.Messaging.Metrics;

/// <summary>Records outbox metrics to OpenTelemetry meters and structured logs.</summary>
public sealed class OpenTelemetryOutboxMetrics(ILogger<OpenTelemetryOutboxMetrics> logger) : IOutboxMetrics
{
    public void RecordClaimed(int count)
    {
        if (count > 0)
        {
            EntitleXMeters.OutboxClaimed.Add(count);
        }

        logger.LogDebug("Outbox metrics: claimed {Count} message(s)", count);
    }

    public void RecordPublished(string messageType, string destinationKind)
    {
        EntitleXMeters.OutboxPublished.Add(
            1,
            new KeyValuePair<string, object?>("message_type", Normalize(messageType)),
            new KeyValuePair<string, object?>("destination_kind", Normalize(destinationKind)));

        logger.LogInformation(
            "Outbox metrics: published {MessageType} via {DestinationKind}",
            messageType,
            destinationKind);
    }

    public void RecordRetry(string messageType, int attempt)
    {
        EntitleXMeters.RetryAttempts.Add(
            1,
            new KeyValuePair<string, object?>("source", "outbox"),
            new KeyValuePair<string, object?>("message_type", Normalize(messageType)));

        logger.LogWarning(
            "Outbox metrics: retry {MessageType} attempt {Attempt}",
            messageType,
            attempt);
    }

    public void RecordPoison(string messageType)
    {
        EntitleXMeters.OutboxPoison.Add(
            1,
            new KeyValuePair<string, object?>("message_type", Normalize(messageType)));

        logger.LogError("Outbox metrics: poison {MessageType}", messageType);
    }

    public void RecordPublishFailure(string messageType)
    {
        EntitleXMeters.OutboxPublishFailures.Add(
            1,
            new KeyValuePair<string, object?>("message_type", Normalize(messageType)));

        logger.LogWarning("Outbox metrics: publish failure {MessageType}", messageType);
    }

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
}
