using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Approval;

/// <summary>Immutable recorded decision for one approval stage.</summary>
public sealed class ApprovalDecision
{
    private ApprovalDecision()
    {
        // EF Core
        ActorUserId = null!;
        ActorRole = null!;
        ActorDisplayName = null!;
    }

    private ApprovalDecision(
        Guid id,
        RequestId requestId,
        int stageOrder,
        ApprovalStageRole stageRole,
        ApprovalDecisionOutcome outcome,
        string actorUserId,
        string actorRole,
        string actorDisplayName,
        string? comment,
        string? correlationId,
        DateTimeOffset decidedAtUtc)
    {
        Id = id;
        RequestId = requestId;
        StageOrder = stageOrder;
        StageRole = stageRole;
        Outcome = outcome;
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        ActorDisplayName = actorDisplayName;
        Comment = comment;
        CorrelationId = correlationId;
        DecidedAtUtc = decidedAtUtc;
    }

    public Guid Id { get; private set; }

    public RequestId RequestId { get; private set; }

    public int StageOrder { get; private set; }

    public ApprovalStageRole StageRole { get; private set; }

    public ApprovalDecisionOutcome Outcome { get; private set; }

    public string ActorUserId { get; private set; }

    public string ActorRole { get; private set; }

    public string ActorDisplayName { get; private set; }

    public string? Comment { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTimeOffset DecidedAtUtc { get; private set; }

    internal static ApprovalDecision Create(
        RequestId requestId,
        int stageOrder,
        ApprovalStageRole stageRole,
        ApprovalDecisionOutcome outcome,
        string actorUserId,
        string actorRole,
        string actorDisplayName,
        string? comment,
        string? correlationId,
        DateTimeOffset decidedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new DomainException("ActorUserId is required for an approval decision.");
        }

        if (string.IsNullOrWhiteSpace(actorRole))
        {
            throw new DomainException("ActorRole is required for an approval decision.");
        }

        if (string.IsNullOrWhiteSpace(actorDisplayName))
        {
            throw new DomainException("ActorDisplayName is required for an approval decision.");
        }

        return new ApprovalDecision(
            Guid.NewGuid(),
            requestId,
            stageOrder,
            stageRole,
            outcome,
            actorUserId.Trim(),
            actorRole.Trim(),
            actorDisplayName.Trim(),
            string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            decidedAtUtc);
    }
}
