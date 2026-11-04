using EntitleX.Messaging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EntitleX.Messaging.Metrics;

public sealed class LoggingOutboxMetrics(ILogger<LoggingOutboxMetrics> logger) : IOutboxMetrics
{
    public void RecordClaimed(int count) =>
        logger.LogDebug("Outbox metrics: claimed {Count} message(s)", count);

    public void RecordPublished(string messageType, string destinationKind) =>
        logger.LogInformation(
            "Outbox metrics: published {MessageType} via {DestinationKind}",
            messageType,
            destinationKind);

    public void RecordRetry(string messageType, int attempt) =>
        logger.LogWarning(
            "Outbox metrics: retry {MessageType} attempt {Attempt}",
            messageType,
            attempt);

    public void RecordPoison(string messageType) =>
        logger.LogError("Outbox metrics: poison {MessageType}", messageType);

    public void RecordPublishFailure(string messageType) =>
        logger.LogWarning("Outbox metrics: publish failure {MessageType}", messageType);
}
