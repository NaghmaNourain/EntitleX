namespace EntitleX.OutboxPublisher;

/// <summary>
/// Phase 1 heartbeat stub. Outbox polling/publishing arrives in a later phase.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxPublisher started (heartbeat stub)");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("OutboxPublisher heartbeat at {Timestamp:u}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
