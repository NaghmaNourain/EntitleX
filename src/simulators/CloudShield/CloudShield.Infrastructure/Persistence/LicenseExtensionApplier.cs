using CloudShield.Application.Abstractions;
using CloudShield.Application.Models;
using CloudShield.Application.Operations;
using CloudShield.Domain.Common;
using CloudShield.Domain.Operations;
using EntitleX.BuildingBlocks.Ids;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CloudShield.Infrastructure.Persistence;

/// <summary>
/// Claims <see cref="LogicalOperationId"/> and updates entitlement expiry in one
/// DB transaction. Does not perform remote HTTP while the transaction is open.
/// </summary>
public sealed class LicenseExtensionApplier : ILicenseExtensionApplier
{
    private readonly CloudShieldDbContext _db;

    public LicenseExtensionApplier(CloudShieldDbContext db)
    {
        _db = db;
    }

    public async Task<LicenseOperationDto> ApplyAsync(
        string entitlementKey,
        SubmitLicenseExtensionRequest request,
        CancellationToken cancellationToken = default)
    {
        var logicalId = LogicalOperationId.From(request.LogicalOperationId);
        var attemptId = AttemptId.From(request.AttemptId);

        var existing = await _db.AppliedLicenseOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.LogicalOperationId == logicalId, cancellationToken);

        if (existing is not null)
        {
            return OperationQueries.ToDto(existing, alreadyApplied: true);
        }

        try
        {
            var claim = await ClaimAndExtendAsync(
                entitlementKey,
                logicalId,
                attemptId,
                request.NewExpiryUtc,
                cancellationToken);

            return OperationQueries.ToDto(claim.Operation, claim.AlreadyApplied);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var replay = await _db.AppliedLicenseOperations
                .AsNoTracking()
                .FirstAsync(o => o.LogicalOperationId == logicalId, cancellationToken);

            return OperationQueries.ToDto(replay, alreadyApplied: true);
        }
    }

    private async Task<(AppliedLicenseOperation Operation, bool AlreadyApplied)> ClaimAndExtendAsync(
        string entitlementKey,
        LogicalOperationId logicalId,
        AttemptId attemptId,
        DateTimeOffset newExpiryUtc,
        CancellationToken cancellationToken)
    {
        AppliedLicenseOperation? operation = null;
        var alreadyApplied = false;

        await _db.ExecuteInTransactionAsync(async ct =>
        {
            var raced = await _db.AppliedLicenseOperations
                .FirstOrDefaultAsync(o => o.LogicalOperationId == logicalId, ct);

            if (raced is not null)
            {
                operation = raced;
                alreadyApplied = true;
                return;
            }

            var entitlement = await _db.Entitlements
                .FirstOrDefaultAsync(e => e.EntitlementKey == entitlementKey, ct)
                ?? throw new DomainException($"Entitlement '{entitlementKey}' was not found.");

            var previous = entitlement.ExpiresAtUtc;
            entitlement.ExtendExpiry(newExpiryUtc);

            operation = AppliedLicenseOperation.CreateApplied(
                logicalId,
                attemptId,
                entitlementKey,
                previous,
                newExpiryUtc);

            _db.AppliedLicenseOperations.Add(operation);
            await _db.SaveChangesAsync(ct);
            alreadyApplied = false;
        }, cancellationToken);

        return (
            operation ?? throw new InvalidOperationException("License extension claim produced no operation."),
            alreadyApplied);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
