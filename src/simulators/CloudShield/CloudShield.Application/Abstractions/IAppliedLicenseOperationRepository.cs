using CloudShield.Domain.Operations;
using EntitleX.SharedKernel.Ids;

namespace CloudShield.Application.Abstractions;

public interface IAppliedLicenseOperationRepository
{
    Task<AppliedLicenseOperation?> GetByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(AppliedLicenseOperation operation, CancellationToken cancellationToken = default);
}
