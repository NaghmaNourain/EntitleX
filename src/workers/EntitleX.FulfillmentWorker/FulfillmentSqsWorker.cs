using EntitleX.Messaging.Options;
using EntitleX.Messaging.Sqs;
using Microsoft.Extensions.Options;

namespace EntitleX.FulfillmentWorker;

/// <summary>
/// Legacy SQS fulfillment dispatcher (opt-in via Messaging:Fulfillment:EnableSqsDispatcher).
/// Extends visibility while processing so large batches do not redeliver mid fan-out.
/// App-level DLQ move after PoisonReceiveCount; infra maxReceiveCount is the secondary owner.
/// </summary>
public sealed class FulfillmentSqsWorker(
    SqsFulfillmentConsumer consumer,
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<FulfillmentSqsWorker> logger) : BackgroundService
{
    private const int PoisonReceiveCount = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Fulfillment SQS worker started (legacy dispatcher)");
        var idle = TimeSpan.FromMilliseconds(
            Math.Clamp(options.Value.Fulfillment.IdleDelayMilliseconds, 100, 10_000));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await consumer.ReceiveAsync(stoppingToken);
                if (messages.Count == 0)
                {
                    await Task.Delay(idle, stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    await HandleOneAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fulfillment SQS loop failure");
                await Task.Delay(idle, stoppingToken);
            }
        }

        logger.LogInformation("Fulfillment SQS worker shutting down gracefully");
    }

    private async Task HandleOneAsync(SqsReceivedMessage message, CancellationToken cancellationToken)
    {
        var visibility = Math.Clamp(options.Value.Sqs.VisibilityTimeoutSeconds, 30, 43200);
        var heartbeatEvery = TimeSpan.FromSeconds(
            Math.Clamp(options.Value.Fulfillment.VisibilityHeartbeatSeconds, 5, visibility / 2));

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(heartbeatEvery, heartbeatCts.Token);
                    await consumer.ExtendVisibilityAsync(message, visibility, heartbeatCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // expected on completion
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "SQS visibility heartbeat failed for {MessageId}",
                    message.Envelope.MessageId);
            }
        }, CancellationToken.None);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<FulfillmentCommandProcessor>();
            await processor.ProcessAsync(message.Envelope, cancellationToken);
            await consumer.DeleteAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed processing fulfillment message {MessageId} (receiveCount={ReceiveCount})",
                message.Envelope.MessageId,
                message.ApproximateReceiveCount);

            if (message.ApproximateReceiveCount >= PoisonReceiveCount)
            {
                await consumer.MoveToDlqAndDeleteAsync(message, ex.Message, cancellationToken);
            }
            // else: leave message — visibility timeout will make it available again
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch
            {
                // ignore
            }
        }
    }
}
