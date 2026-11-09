using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Idempotently records provider completion/failure events and advances the
/// durable saga (which aggregates request status via forward recovery).
/// Claim key includes AttemptId so selective retries can settle.
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
        var key = ResultClaimKey(payload.LogicalOperationId, payload.AttemptId);
        if (!await TryStageClaimAsync(key, envelope.MessageType, cancellationToken))
        {
            return;
        }

        StageResult(new FulfillmentItemResult(
            payload.RequestId,
            payload.RequestItemId,
            payload.LogicalOperationId,
            payload.AttemptId,
            payload.EntitlementId,
            payload.Provider,
            succeeded: true,
            errorCode: null,
            errorMessage: null,
            DateTimeOffset.UtcNow));

        await _sagaOrchestrator.OnItemCompletedAsync(
            RequestId.From(payload.RequestId),
            LogicalOperationId.From(payload.LogicalOperationId),
            envelope.CorrelationId?.ToString("D"),
            cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            _logger.LogDebug("Skipping duplicate provider result {Key}", key);
            return;
        }

        _logger.LogInformation(
            "Recorded success for request {RequestId} logicalOp={LogicalOperationId} attempt={AttemptId}",
            payload.RequestId,
            payload.LogicalOperationId,
            payload.AttemptId);
    }

    public async Task ProcessFailedAsync(
        MessageEnvelope envelope,
        ProviderLicenseExtensionFailedV1 payload,
        CancellationToken cancellationToken = default)
    {
        var key = ResultClaimKey(payload.LogicalOperationId, payload.AttemptId);
        if (!await TryStageClaimAsync(key, envelope.MessageType, cancellationToken))
        {
            return;
        }

        StageResult(new FulfillmentItemResult(
            payload.RequestId,
            payload.RequestItemId,
            payload.LogicalOperationId,
            payload.AttemptId,
            payload.EntitlementId,
            payload.Provider,
            succeeded: false,
            payload.ErrorCode,
            payload.ErrorMessage,
            DateTimeOffset.UtcNow));

        await _sagaOrchestrator.OnItemFailedAsync(
            RequestId.From(payload.RequestId),
            LogicalOperationId.From(payload.LogicalOperationId),
            payload.ErrorCode,
            payload.ErrorMessage,
            envelope.CorrelationId?.ToString("D"),
            cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            _logger.LogDebug("Skipping duplicate provider result {Key}", key);
            return;
        }

        _logger.LogInformation(
            "Recorded failure for request {RequestId} logicalOp={LogicalOperationId} attempt={AttemptId}",
            payload.RequestId,
            payload.LogicalOperationId,
            payload.AttemptId);
    }

    public static string ResultClaimKey(Guid logicalOperationId, Guid attemptId) =>
        $"result:{logicalOperationId:D}:{attemptId:D}";

    /// <summary>
    /// Stages a processed_messages row without flushing. Returns false when the key
    /// is already present (DB or local tracker).
    /// </summary>
    private async Task<bool> TryStageClaimAsync(
        string idempotencyKey,
        string messageType,
        CancellationToken cancellationToken)
    {
        if (_db.ProcessedMessages.Local.Any(x =>
                x.ConsumerName == ConsumerName && x.IdempotencyKey == idempotencyKey))
        {
            return false;
        }

        var exists = await _db.ProcessedMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.ConsumerName == ConsumerName && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.ProcessedMessages.Add(new ProcessedMessage(
            ConsumerName,
            idempotencyKey,
            DateTimeOffset.UtcNow,
            messageType));
        return true;
    }

    private void StageResult(FulfillmentItemResult result)
    {
        _db.FulfillmentItemResults.Add(result);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
