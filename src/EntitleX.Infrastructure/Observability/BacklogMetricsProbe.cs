using EntitleX.BuildingBlocks.Observability;
using EntitleX.Domain.Saga;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EntitleX.Infrastructure.Observability;

/// <summary>
/// Periodically refreshes outbox + reconciliation backlog gauges (no ID labels).
/// </summary>
public sealed class BacklogMetricsProbe(
    IServiceScopeFactory scopeFactory,
    ILogger<BacklogMetricsProbe> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Backlog metrics probe failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();

        EntitleXMeters.OutboxBacklogValue = await db.OutboxMessages
            .AsNoTracking()
            .CountAsync(
                m => m.Status == OutboxMessageStatus.Pending
                     || m.Status == OutboxMessageStatus.Processing,
                cancellationToken);

        EntitleXMeters.ReconciliationBacklogValue = await db.LicenseExtensionSagas
            .AsNoTracking()
            .CountAsync(
                s => s.State == SagaState.ReconciliationRequired
                     || s.ItemOutcomes.Any(o => o.Status == SagaItemOutcomeStatus.Uncertain),
                cancellationToken);
    }
}
