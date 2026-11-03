namespace EntitleX.NotificationService;

/// <summary>
/// Phase 1 heartbeat stub. Notification dispatch arrives in a later phase.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NotificationService started (heartbeat stub)");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("NotificationService heartbeat at {Timestamp:u}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
