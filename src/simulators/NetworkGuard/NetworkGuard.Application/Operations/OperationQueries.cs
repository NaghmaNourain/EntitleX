using EntitleX.SharedKernel.Ids;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Application.Models;
using NetworkGuard.Domain.Operations;

namespace NetworkGuard.Application.Operations;

public sealed class OperationQueries
{
    private readonly IAppliedLicenseOperationRepository _repository;

    public OperationQueries(IAppliedLicenseOperationRepository repository)
    {
        _repository = repository;
    }

    public async Task<LicenseOperationDto?> GetByLogicalOperationIdAsync(
        Guid logicalOperationId,
        CancellationToken cancellationToken = default)
    {
        var op = await _repository.GetByLogicalOperationIdAsync(
            LogicalOperationId.From(logicalOperationId),
            cancellationToken);

        return op is null ? null : ToDto(op, alreadyApplied: true);
    }

    public static LicenseOperationDto ToDto(AppliedLicenseOperation op, bool alreadyApplied) =>
        new(
            op.LogicalOperationId.Value,
            op.AttemptId.Value,
            op.LicenseId,
            op.Status.ToString(),
            op.PreviousExpiryUtc,
            op.NewExpiryUtc,
            op.CreatedAtUtc,
            op.CompletedAtUtc,
            op.FailureReason,
            alreadyApplied);
}
