using EntitleX.SharedKernel.Ids;
using Microsoft.EntityFrameworkCore;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Application.Models;
using NetworkGuard.Application.Operations;
using NetworkGuard.Domain.Common;
using NetworkGuard.Domain.Operations;
using Npgsql;

namespace NetworkGuard.Infrastructure.Persistence;

/// <summary>
/// Claims LogicalOperationId and updates license validity in one DB transaction.
/// Never holds the transaction across remote HTTP.
/// </summary>
public sealed class LicenseExtensionApplier : ILicenseExtensionApplier
{
    private readonly NetworkGuardDbContext _db;

    public LicenseExtensionApplier(NetworkGuardDbContext db)
    {
        _db = db;
    }

    public async Task<LicenseOperationDto> ApplyAsync(
        string licenseId,
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
                licenseId,
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
        string licenseId,
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

            var license = await _db.Licenses
                .FirstOrDefaultAsync(e => e.LicenseId == licenseId, ct)
                ?? throw new DomainException($"License '{licenseId}' was not found.");

            var previous = license.ValidUntilUtc;
            license.ExtendValidity(newExpiryUtc);

            operation = AppliedLicenseOperation.CreateApplied(
                logicalId,
                attemptId,
                licenseId,
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
