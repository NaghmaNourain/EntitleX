using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Idempotently records provider completion/failure events and advances request
/// status when all items have reported (pre-saga ledger).
/// </summary>
public sealed class ProviderResultProcessor
{
    public const string ConsumerName = "entitlex-provider-results";

    private readonly EntitleXDbContext _db;
    private readonly ILogger<ProviderResultProcessor> _logger;

    public ProviderResultProcessor(EntitleXDbContext db, ILogger<ProviderResultProcessor> logger)
    {
        _db = db;
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

        await TryCompleteRequestAsync(payload.RequestId, cancellationToken);
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

        await TryCompleteRequestAsync(payload.RequestId, cancellationToken);
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

    private async Task TryCompleteRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await _db.LicenseExtensionRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == RequestId.From(requestId), cancellationToken);

        if (request is null)
        {
            _logger.LogWarning("Provider result for unknown request {RequestId}", requestId);
            return;
        }

        var results = await _db.FulfillmentItemResults
            .Where(r => r.RequestId == requestId)
            .ToListAsync(cancellationToken);

        if (results.Count < request.Items.Count)
        {
            return;
        }

        var succeeded = results.Count(r => r.Succeeded);
        var failed = results.Count - succeeded;

        try
        {
            request.CompleteFulfillment(succeeded, failed);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Request {RequestId} fulfillment settled succeeded={Succeeded} failed={Failed} status={Status}",
                requestId,
                succeeded,
                failed,
                request.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not settle fulfillment for request {RequestId} (status={Status})",
                requestId,
                request.Status);
        }
    }
}
