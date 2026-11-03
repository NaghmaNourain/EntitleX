namespace EntitleX.ReconciliationWorker;

/// <summary>
/// Phase 1 heartbeat stub. License reconciliation arrives in a later phase.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReconciliationWorker started (heartbeat stub)");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("ReconciliationWorker heartbeat at {Timestamp:u}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
