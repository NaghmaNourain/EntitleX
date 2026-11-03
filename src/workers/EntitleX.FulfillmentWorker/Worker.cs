namespace EntitleX.FulfillmentWorker;

/// <summary>
/// Phase 1 heartbeat stub. Fulfillment orchestration arrives in a later phase.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FulfillmentWorker started (heartbeat stub)");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("FulfillmentWorker heartbeat at {Timestamp:u}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
