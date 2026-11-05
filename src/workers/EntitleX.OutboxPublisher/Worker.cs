using EntitleX.Infrastructure.Outbox;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;

namespace EntitleX.OutboxPublisher;

/// <summary>
/// Polls the transactional outbox with FOR UPDATE SKIP LOCKED and publishes to SQS/Kafka.
/// </summary>
public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxPublisher started");
        var pollDelay = TimeSpan.FromMilliseconds(
            Math.Clamp(options.Value.Outbox.PollIntervalMilliseconds, 100, 30_000));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var publisher = scope.ServiceProvider.GetRequiredService<OutboxPublisherService>();
                var count = await publisher.PublishOnceAsync(stoppingToken);
                if (count == 0)
                {
                    await Task.Delay(pollDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxPublisher loop failure; backing off");
                try
                {
                    await Task.Delay(pollDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("OutboxPublisher shutting down gracefully");
    }
}
