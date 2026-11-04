using EntitleX.Application.Abstractions;

namespace EntitleX.ReconciliationWorker;

/// <summary>
/// Phase 6 wires saga timeout-hook scanning. Full provider reconciliation
/// repair arrives in Phase 8.
/// </summary>
public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "ReconciliationWorker started (Phase 6: saga timeout hooks; Phase 8: full repair TBD)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider
                    .GetRequiredService<ILicenseExtensionSagaOrchestrator>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                await orchestrator.ApplyElapsedTimeoutsAsync(
                    DateTimeOffset.UtcNow,
                    take: 50,
                    stoppingToken);
                await unitOfWork.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Saga timeout-hook scan failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
