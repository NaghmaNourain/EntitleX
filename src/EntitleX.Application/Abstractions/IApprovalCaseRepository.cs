using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;

namespace EntitleX.Application.Abstractions;

public interface IApprovalCaseRepository
{
    Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default);

    Task<ApprovalCase?> GetByRequestIdAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);

    Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
