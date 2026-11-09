using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;

namespace EntitleX.Application.Abstractions;

public interface IApprovalCaseRepository
{
    Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default);

    Task<ApprovalCase?> GetByRequestIdAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);

    Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Pending approval cases (for scheduled reminder scans).</summary>
    Task<IReadOnlyList<ApprovalCase>> ListPendingAsync(
        int take,
        CancellationToken cancellationToken = default);
}
