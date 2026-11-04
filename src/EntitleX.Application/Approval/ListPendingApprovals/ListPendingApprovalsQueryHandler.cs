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
        // Prefer keyset (AfterCreatedAtUtc + AfterApprovalCaseId) over Skip for stable paging.
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

        var ordered = filtered
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .ToList();

        IEnumerable<ApprovalCase> pageSource = ordered;
        if (query.AfterCreatedAtUtc is not null && query.AfterApprovalCaseId is not null)
        {
            var cursorTime = query.AfterCreatedAtUtc.Value;
            var cursorId = query.AfterApprovalCaseId.Value;
            pageSource = ordered.Where(c =>
                c.CreatedAtUtc > cursorTime
                || (c.CreatedAtUtc == cursorTime && c.Id.CompareTo(cursorId) > 0));
        }
        else if (skip > 0)
        {
            pageSource = ordered.Skip(skip);
        }

        var pageCases = pageSource.Take(take).ToList();
        var page = pageCases
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

        DateTimeOffset? nextCreated = null;
        Guid? nextId = null;
        if (pageCases.Count == take && pageCases.Count > 0)
        {
            var last = pageCases[^1];
            nextCreated = last.CreatedAtUtc;
            nextId = last.Id;
        }

        return new PendingApprovalsPageDto(ordered.Count, skip, take, page, nextCreated, nextId);
    }
}
