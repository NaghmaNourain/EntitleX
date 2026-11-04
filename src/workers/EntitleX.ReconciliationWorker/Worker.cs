using EntitleX.Application.Abstractions;

namespace EntitleX.ReconciliationWorker;

/// <summary>
/// Scheduled reconciliation: applies elapsed saga timeouts, then probes providers
/// for uncertain / missing outcomes and repairs local state. Never blind-retries
/// unknown external effects.
/// </summary>
public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "ReconciliationWorker started (Phase 8: timeout hooks + provider repair)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reconciliation = scope.ServiceProvider
                    .GetRequiredService<IReconciliationService>();

                var results = await reconciliation.RunBatchAsync(
                    take: 50,
                    actor: "reconciliation-worker",
                    correlationId: null,
                    stoppingToken);

                if (results.Count > 0)
                {
                    var stillOpen = results.Count(r => r.StillRequiresReconciliation);
                    logger.LogInformation(
                        "Reconciliation batch processed {Count} request(s); {Open} still require reconciliation",
                        results.Count,
                        stillOpen);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reconciliation batch failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
