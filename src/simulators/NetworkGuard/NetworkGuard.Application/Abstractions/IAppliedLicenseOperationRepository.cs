using EntitleX.SharedKernel.Ids;
using NetworkGuard.Domain.Operations;

namespace NetworkGuard.Application.Abstractions;

public interface IAppliedLicenseOperationRepository
{
    Task<AppliedLicenseOperation?> GetByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(AppliedLicenseOperation operation, CancellationToken cancellationToken = default);
}
