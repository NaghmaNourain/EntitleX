using EntitleX.Messaging.Sqs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EntitleX.Infrastructure.Observability;

/// <summary>Refreshes SQS approximate depth gauge on a fixed interval.</summary>
public sealed class SqsDepthProbeHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SqsDepthProbeHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var probe = scope.ServiceProvider.GetService<SqsDepthProbe>();
                if (probe is not null)
                {
                    await probe.RefreshAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "SQS depth probe hosted refresh failed");
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
}
