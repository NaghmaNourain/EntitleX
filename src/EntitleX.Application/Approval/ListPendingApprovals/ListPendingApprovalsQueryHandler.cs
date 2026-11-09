using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using MediatR;

namespace EntitleX.Application.Approval.ListPendingApprovals;

public sealed class ListPendingApprovalsQueryHandler
    : IRequestHandler<ListPendingApprovalsQuery, PendingApprovalsPageDto>
{
    private readonly IApprovalCaseRepository _approvals;
    private readonly ICurrentUser _currentUser;

    public ListPendingApprovalsQueryHandler(
        IApprovalCaseRepository approvals,
        ICurrentUser currentUser)
    {
        _approvals = approvals;
        _currentUser = currentUser;
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

        var allowedRoles = ResolveAllowedRoleFilters(query.Role);
        // Empty allowed set means the caller has no stage visibility — never fall open to all.
        IEnumerable<ApprovalCase> filtered = pending.Where(c =>
        {
            if (allowedRoles.Count == 0)
            {
                return false;
            }

            var stage = c.CurrentStage;
            if (stage is null)
            {
                return false;
            }

            var required = ApprovalRules.RoleName(stage.RequiredRole);
            return allowedRoles.Contains(required, StringComparer.OrdinalIgnoreCase);
        });

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

    /// <summary>
    /// Ignore client Role when it is not in the caller's roles; default to the caller's
    /// stage-capable roles so representatives cannot spoof into manager queues.
    /// PlatformAdmin may filter any stage role (or omit Role for all pending).
    /// </summary>
    private HashSet<string> ResolveAllowedRoleFilters(string? requestedRole)
    {
        var callerRoles = _currentUser.Roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isAdmin = callerRoles.Contains(EntitleXRoles.PlatformAdmin);
        var stageCapable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (callerRoles.Contains(EntitleXRoles.RenewalManager) || isAdmin)
        {
            stageCapable.Add(EntitleXRoles.RenewalManager);
        }

        if (callerRoles.Contains(EntitleXRoles.DealDeskManager) || isAdmin)
        {
            stageCapable.Add(EntitleXRoles.DealDeskManager);
        }

        if (!string.IsNullOrWhiteSpace(requestedRole))
        {
            var role = requestedRole.Trim();
            if (isAdmin || callerRoles.Contains(role))
            {
                // Admin or genuine role holder may narrow to that role when it is a stage role.
                if (string.Equals(role, EntitleXRoles.RenewalManager, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(role, EntitleXRoles.DealDeskManager, StringComparison.OrdinalIgnoreCase))
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role };
                }
            }

            // Spoofed / non-owned role: fall through to caller stage defaults (may be empty).
        }

        return stageCapable;
    }
}
