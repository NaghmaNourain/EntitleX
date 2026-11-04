using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
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
}
