using EntitleX.Messaging.Options;
using EntitleX.Messaging.Sqs;
using Microsoft.Extensions.Options;

namespace EntitleX.FulfillmentWorker;

/// <summary>Consumes fulfillment commands from SQS with visibility timeout + DLQ handling.</summary>
public sealed class FulfillmentSqsWorker(
    SqsFulfillmentConsumer consumer,
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingOptions> options,
    ILogger<FulfillmentSqsWorker> logger) : BackgroundService
{
    private const int PoisonReceiveCount = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Fulfillment SQS worker started");
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
    }
}
