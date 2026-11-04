using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using MediatR;

namespace EntitleX.Application.Approval.ListPendingApprovals;

public sealed class ListPendingApprovalsQueryHandler
    : IRequestHandler<ListPendingApprovalsQuery, PendingApprovalsPageDto>
{
    private readonly IApprovalCaseRepository _approvals;

    public ListPendingApprovalsQueryHandler(IApprovalCaseRepository approvals)
    {
        _approvals = approvals;
    }

    public async Task<PendingApprovalsPageDto> Handle(
        ListPendingApprovalsQuery query,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, 200);
        var skip = Math.Max(0, query.Skip);

        // Fetch a bounded pending set, then filter/page in memory (pending queues are small).
        var pending = await _approvals.ListPendingAsync(500, cancellationToken);

        IEnumerable<ApprovalCase> filtered = pending;
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();
            filtered = pending.Where(c =>
            {
                var stage = c.CurrentStage;
                return stage is not null
                    && string.Equals(
                        ApprovalRules.RoleName(stage.RequiredRole),
                        role,
                        StringComparison.OrdinalIgnoreCase);
            });
        }

        var materialized = filtered.ToList();
        var page = materialized
            .Skip(skip)
            .Take(take)
            .Select(c => new PendingApprovalSummaryDto(
                c.Id,
                c.RequestId.Value,
                c.MaxExtensionDays,
                c.Band.ToString(),
                c.Status.ToString(),
                c.CurrentStageOrder,
                c.CurrentStage is { } stage
                    ? ApprovalRules.RoleName(stage.RequiredRole)
                    : null,
                c.CreatedAtUtc))
            .ToList();

        return new PendingApprovalsPageDto(materialized.Count, skip, take, page);
    }
}
