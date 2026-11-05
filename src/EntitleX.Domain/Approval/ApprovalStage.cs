using EntitleX.Domain.Common;

namespace EntitleX.Domain.Approval;

/// <summary>One sequential stage within an <see cref="ApprovalCase"/>.</summary>
public sealed class ApprovalStage
{
    private ApprovalStage()
    {
        // EF Core
    }

    private ApprovalStage(Guid id, int order, ApprovalStageRole requiredRole)
    {
        Id = id;
        Order = order;
        RequiredRole = requiredRole;
        Status = ApprovalStageStatus.Pending;
        DecisionId = null;
    }

    public Guid Id { get; private set; }

    public int Order { get; private set; }

    public ApprovalStageRole RequiredRole { get; private set; }

    public ApprovalStageStatus Status { get; private set; }

    public Guid? DecisionId { get; private set; }

    internal static ApprovalStage Create(int order, ApprovalStageRole requiredRole)
    {
        if (order < 0)
        {
            throw new DomainException("Approval stage order cannot be negative.");
        }

        return new ApprovalStage(Guid.NewGuid(), order, requiredRole);
    }

    internal void RecordDecision(Guid decisionId, ApprovalDecisionOutcome outcome)
    {
        if (Status != ApprovalStageStatus.Pending)
        {
            throw new DomainException(
                $"Cannot record a decision on stage {Order} in status '{Status}'.");
        }

        DecisionId = decisionId;
        Status = outcome switch
        {
            ApprovalDecisionOutcome.Approved => ApprovalStageStatus.Approved,
            ApprovalDecisionOutcome.Rejected => ApprovalStageStatus.Rejected,
            _ => throw new DomainException($"Unknown decision outcome '{outcome}'.")
        };
    }
}
