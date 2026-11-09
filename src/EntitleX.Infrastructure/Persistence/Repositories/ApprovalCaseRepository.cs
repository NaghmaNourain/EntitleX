using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Persistence.Repositories;

public sealed class ApprovalCaseRepository : IApprovalCaseRepository
{
    private readonly EntitleXDbContext _dbContext;

    public ApprovalCaseRepository(EntitleXDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default)
    {
        await _dbContext.ApprovalCases.AddAsync(approvalCase, cancellationToken);
    }

    public Task<ApprovalCase?> GetByRequestIdAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default) =>
        _dbContext.ApprovalCases
            .Include(x => x.Stages)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);

    public Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.ApprovalCases
            .Include(x => x.Stages)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApprovalCase>> ListPendingAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(take, 1, 500);
        return await _dbContext.ApprovalCases
            .Include(x => x.Stages)
            .Include(x => x.Decisions)
            .Where(x => x.Status == ApprovalCaseStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
