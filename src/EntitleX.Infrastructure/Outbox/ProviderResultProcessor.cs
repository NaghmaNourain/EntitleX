using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Idempotently records provider completion/failure events and advances the
/// durable saga (which aggregates request status via forward recovery).
/// </summary>
public sealed class ProviderResultProcessor
{
    public const string ConsumerName = "entitlex-provider-results";

    private readonly EntitleXDbContext _db;
    private readonly ILicenseExtensionSagaOrchestrator _sagaOrchestrator;
    private readonly ILogger<ProviderResultProcessor> _logger;

    public ProviderResultProcessor(
        EntitleXDbContext db,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        ILogger<ProviderResultProcessor> logger)
    {
        _db = db;
        _sagaOrchestrator = sagaOrchestrator;
        _logger = logger;
    }

    public async Task ProcessCompletedAsync(
        MessageEnvelope envelope,
        ProviderLicenseExtensionCompletedV1 payload,
        CancellationToken cancellationToken = default)
    {
        var key = $"result:{payload.LogicalOperationId:D}";
        if (!await TryClaimAsync(key, envelope.MessageType, cancellationToken))
        {
            return;
        }

        await RecordResultAsync(
            new FulfillmentItemResult(
                payload.RequestId,
                payload.RequestItemId,
                payload.LogicalOperationId,
                payload.EntitlementId,
                payload.Provider,
                succeeded: true,
                errorCode: null,
                errorMessage: null,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await _sagaOrchestrator.OnItemCompletedAsync(
            RequestId.From(payload.RequestId),
            LogicalOperationId.From(payload.LogicalOperationId),
            envelope.CorrelationId?.ToString("D"),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded success for request {RequestId} logicalOp={LogicalOperationId}",
            payload.RequestId,
            payload.LogicalOperationId);
    }

    public async Task ProcessFailedAsync(
        MessageEnvelope envelope,
        ProviderLicenseExtensionFailedV1 payload,
        CancellationToken cancellationToken = default)
    {
        var key = $"result:{payload.LogicalOperationId:D}";
        if (!await TryClaimAsync(key, envelope.MessageType, cancellationToken))
        {
            return;
        }

        await RecordResultAsync(
            new FulfillmentItemResult(
                payload.RequestId,
                payload.RequestItemId,
                payload.LogicalOperationId,
                payload.EntitlementId,
                payload.Provider,
                succeeded: false,
                payload.ErrorCode,
                payload.ErrorMessage,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await _sagaOrchestrator.OnItemFailedAsync(
            RequestId.From(payload.RequestId),
            LogicalOperationId.From(payload.LogicalOperationId),
            payload.ErrorCode,
            payload.ErrorMessage,
            envelope.CorrelationId?.ToString("D"),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded failure for request {RequestId} logicalOp={LogicalOperationId}",
            payload.RequestId,
            payload.LogicalOperationId);
    }

    private async Task<bool> TryClaimAsync(
        string idempotencyKey,
        string messageType,
        CancellationToken cancellationToken)
    {
        _db.ProcessedMessages.Add(new ProcessedMessage(
            ConsumerName,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            messageType));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            _db.ChangeTracker.Clear();
            _logger.LogDebug("Skipping duplicate provider result {Key}", idempotencyKey);
            return false;
        }
    }

    private async Task RecordResultAsync(
        FulfillmentItemResult result,
        CancellationToken cancellationToken)
    {
        _db.FulfillmentItemResults.Add(result);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            _db.ChangeTracker.Clear();
        }
    }
}
