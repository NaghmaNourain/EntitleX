using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EntitleX.Infrastructure.Outbox;

public sealed class EfLocalFulfillmentOutcomeStore : ILocalFulfillmentOutcomeStore
{
    private readonly EntitleXDbContext _db;
    private readonly ILogger<EfLocalFulfillmentOutcomeStore> _logger;

    public EfLocalFulfillmentOutcomeStore(
        EntitleXDbContext db,
        ILogger<EfLocalFulfillmentOutcomeStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<LocalFulfillmentOutcome?> FindByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.FulfillmentItemResults
            .AsNoTracking()
            .Where(r => r.LogicalOperationId == logicalOperationId.Value)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var preferred = rows.FirstOrDefault(r => r.Succeeded)
            ?? rows.OrderByDescending(r => r.RecordedAtUtc).First();

        return ToModel(preferred);
    }

    public async Task<IReadOnlyList<LocalFulfillmentOutcome>> ListByRequestAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.FulfillmentItemResults
            .AsNoTracking()
            .Where(r => r.RequestId == requestId.Value)
            .ToListAsync(cancellationToken);

        return rows.Select(ToModel).ToList();
    }

    public async Task<bool> TryRecordAsync(
        LocalFulfillmentOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        _db.FulfillmentItemResults.Add(new FulfillmentItemResult(
            outcome.RequestId,
            outcome.RequestItemId,
            outcome.LogicalOperationId,
            outcome.AttemptId,
            outcome.EntitlementId,
            outcome.Provider,
            outcome.Succeeded,
            outcome.ErrorCode,
            outcome.ErrorMessage,
            outcome.RecordedAtUtc));

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
            _logger.LogDebug(
                "Fulfillment outcome already recorded for logicalOp={LogicalOperationId} attempt={AttemptId}",
                outcome.LogicalOperationId,
                outcome.AttemptId);
            return false;
        }
    }

    private static LocalFulfillmentOutcome ToModel(FulfillmentItemResult row) =>
        new(
            row.RequestId,
            row.RequestItemId,
            row.LogicalOperationId,
            row.AttemptId,
            row.EntitlementId,
            row.Provider,
            row.Succeeded,
            row.ErrorCode,
            row.ErrorMessage,
            row.RecordedAtUtc);
}
