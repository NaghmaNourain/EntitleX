using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Common;
using EntitleX.Domain.Identity;

namespace EntitleX.Domain.Approval;

/// <summary>
/// Approval workflow for a single license-extension request.
/// Enforces sequential stages and role-gated decisions.
/// </summary>
public sealed class ApprovalCase : AggregateRoot
{
    private readonly List<ApprovalStage> _stages = [];
    private readonly List<ApprovalDecision> _decisions = [];

    private ApprovalCase()
    {
        // EF Core
        Band = default;
    }

    private ApprovalCase(
        Guid id,
        RequestId requestId,
        int maxExtensionDays,
        ApprovalBand band,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        RequestId = requestId;
        MaxExtensionDays = maxExtensionDays;
        Band = band;
        Status = band == ApprovalBand.Unsupported
            ? ApprovalCaseStatus.Unsupported
            : ApprovalCaseStatus.Pending;
        CurrentStageOrder = 0;
        CreatedAtUtc = createdAtUtc;
        CompletedAtUtc = null;
    }

    public Guid Id { get; private set; }

    public RequestId RequestId { get; private set; }

    public int MaxExtensionDays { get; private set; }

    public ApprovalBand Band { get; private set; }

    public ApprovalCaseStatus Status { get; private set; }

    /// <summary>Zero-based index of the active pending stage; -1 when complete / auto.</summary>
    public int CurrentStageOrder { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public IReadOnlyList<ApprovalStage> Stages => _stages.AsReadOnly();

    public IReadOnlyList<ApprovalDecision> Decisions => _decisions.AsReadOnly();

    public ApprovalStage? CurrentStage =>
        Status == ApprovalCaseStatus.Pending
            ? _stages.SingleOrDefault(s => s.Order == CurrentStageOrder)
            : null;

    /// <summary>
    /// Opens an approval case for the given band. Auto-approve and unsupported
    /// cases complete immediately without human stages.
    /// </summary>
    public static ApprovalCase Open(
        RequestId requestId,
        int maxExtensionDays,
        ApprovalBand band,
        DateTimeOffset? createdAtUtc = null)
    {
        if (maxExtensionDays < ApprovalRules.MinSupportedDays && band != ApprovalBand.Unsupported)
        {
            // Still allow constructing unsupported / evaluated bands from rules.
        }

        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        var approvalCase = new ApprovalCase(
            Guid.NewGuid(),
            requestId,
            maxExtensionDays,
            band,
            now);

        var required = ApprovalRules.RequiredStages(band);
        for (var i = 0; i < required.Count; i++)
        {
            approvalCase._stages.Add(ApprovalStage.Create(i, required[i]));
        }

        if (band == ApprovalBand.AutoApprove)
        {
            approvalCase.Status = ApprovalCaseStatus.Approved;
            approvalCase.CurrentStageOrder = -1;
            approvalCase.CompletedAtUtc = now;
        }
        else if (band == ApprovalBand.Unsupported)
        {
            approvalCase.Status = ApprovalCaseStatus.Unsupported;
            approvalCase.CurrentStageOrder = -1;
            approvalCase.CompletedAtUtc = now;
        }
        else
        {
            approvalCase.Status = ApprovalCaseStatus.Pending;
            approvalCase.CurrentStageOrder = 0;
        }

        return approvalCase;
    }

    /// <summary>
    /// Records an approve/reject for the current stage. Only the required role
    /// (or PlatformAdmin) may decide. Unauthorized actors throw
    /// <see cref="UnauthorizedApprovalTransitionException"/>.
    /// </summary>
    public ApprovalDecision RecordDecision(
        ApprovalDecisionOutcome outcome,
        string actorUserId,
        IEnumerable<string> actorRoles,
        string actorDisplayName,
        string? comment,
        string? correlationId,
        DateTimeOffset? decidedAtUtc = null)
    {
        if (Status != ApprovalCaseStatus.Pending)
        {
            throw new DomainException(
                $"Cannot record a decision on approval case in status '{Status}'.");
        }

        var stage = CurrentStage
            ?? throw new DomainException("No current approval stage is pending.");

        var roles = actorRoles?.Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? throw new DomainException("Actor roles are required.");

        var requiredRoleName = ApprovalRules.RoleName(stage.RequiredRole);
        var isAuthorized = roles.Contains(requiredRoleName)
            || roles.Contains(EntitleXRoles.PlatformAdmin);

        if (!isAuthorized)
        {
            throw new UnauthorizedApprovalTransitionException(
                $"Actor roles [{string.Join(", ", roles)}] cannot decide stage " +
                $"{stage.Order} which requires '{requiredRoleName}'.");
        }

        var effectiveRole = roles.Contains(requiredRoleName)
            ? requiredRoleName
            : EntitleXRoles.PlatformAdmin;

        var now = decidedAtUtc ?? DateTimeOffset.UtcNow;
        var decision = ApprovalDecision.Create(
            RequestId,
            stage.Order,
            stage.RequiredRole,
            outcome,
            actorUserId,
            effectiveRole,
            actorDisplayName,
            comment,
            correlationId,
            now);

        stage.RecordDecision(decision.Id, outcome);
        _decisions.Add(decision);

        if (outcome == ApprovalDecisionOutcome.Rejected)
        {
            Status = ApprovalCaseStatus.Rejected;
            CurrentStageOrder = -1;
            CompletedAtUtc = now;
            return decision;
        }

        var next = _stages.FirstOrDefault(s => s.Order == stage.Order + 1);
        if (next is null)
        {
            Status = ApprovalCaseStatus.Approved;
            CurrentStageOrder = -1;
            CompletedAtUtc = now;
        }
        else
        {
            CurrentStageOrder = next.Order;
        }

        return decision;
    }
}
