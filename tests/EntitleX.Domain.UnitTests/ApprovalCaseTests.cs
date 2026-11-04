using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using EntitleX.Domain.Identity;

namespace EntitleX.Domain.UnitTests;

public sealed class ApprovalCaseTests
{
    [Fact]
    public void Open_AutoApprove_CompletesWithoutStages()
    {
        var approval = ApprovalCase.Open(RequestId.New(), 15, ApprovalBand.AutoApprove);

        Assert.Equal(ApprovalCaseStatus.Approved, approval.Status);
        Assert.Empty(approval.Stages);
        Assert.Equal(-1, approval.CurrentStageOrder);
        Assert.NotNull(approval.CompletedAtUtc);
    }

    [Fact]
    public void Open_Unsupported_CompletesAsUnsupported()
    {
        var approval = ApprovalCase.Open(RequestId.New(), 400, ApprovalBand.Unsupported);

        Assert.Equal(ApprovalCaseStatus.Unsupported, approval.Status);
        Assert.Empty(approval.Stages);
    }

    [Fact]
    public void RecordDecision_SequentialRmThenDealDesk()
    {
        var approval = ApprovalCase.Open(
            RequestId.New(),
            120,
            ApprovalBand.RenewalManagerThenDealDesk);

        Assert.Equal(2, approval.Stages.Count);
        Assert.Equal(0, approval.CurrentStageOrder);

        approval.RecordDecision(
            ApprovalDecisionOutcome.Approved,
            "user-rm-001",
            [EntitleXRoles.RenewalManager],
            "Morgan Manager",
            "ok",
            "corr-1");

        Assert.Equal(ApprovalCaseStatus.Pending, approval.Status);
        Assert.Equal(1, approval.CurrentStageOrder);
        Assert.Equal(ApprovalStageRole.DealDeskManager, approval.CurrentStage!.RequiredRole);

        approval.RecordDecision(
            ApprovalDecisionOutcome.Approved,
            "user-dd-001",
            [EntitleXRoles.DealDeskManager],
            "Dana DealDesk",
            null,
            "corr-2");

        Assert.Equal(ApprovalCaseStatus.Approved, approval.Status);
        Assert.Equal(2, approval.Decisions.Count);
        Assert.Equal(-1, approval.CurrentStageOrder);
    }

    [Fact]
    public void RecordDecision_WhenWrongRole_ThrowsUnauthorized()
    {
        var approval = ApprovalCase.Open(
            RequestId.New(),
            60,
            ApprovalBand.RenewalManagerOnly);

        var ex = Assert.Throws<UnauthorizedApprovalTransitionException>(() =>
            approval.RecordDecision(
                ApprovalDecisionOutcome.Approved,
                "user-dd-001",
                [EntitleXRoles.DealDeskManager],
                "Dana DealDesk",
                null,
                null));

        Assert.Contains("RenewalManager", ex.Message, StringComparison.Ordinal);
        Assert.Equal(ApprovalCaseStatus.Pending, approval.Status);
        Assert.Empty(approval.Decisions);
    }

    [Fact]
    public void RecordDecision_PlatformAdmin_CanActForAnyStage()
    {
        var approval = ApprovalCase.Open(
            RequestId.New(),
            60,
            ApprovalBand.RenewalManagerOnly);

        approval.RecordDecision(
            ApprovalDecisionOutcome.Approved,
            "user-admin-001",
            [EntitleXRoles.PlatformAdmin],
            "Alex Admin",
            "admin override",
            null);

        Assert.Equal(ApprovalCaseStatus.Approved, approval.Status);
        Assert.Equal(EntitleXRoles.PlatformAdmin, approval.Decisions[0].ActorRole);
    }

    [Fact]
    public void RecordDecision_Reject_CompletesCase()
    {
        var approval = ApprovalCase.Open(
            RequestId.New(),
            120,
            ApprovalBand.RenewalManagerThenDealDesk);

        approval.RecordDecision(
            ApprovalDecisionOutcome.Rejected,
            "user-rm-001",
            [EntitleXRoles.RenewalManager],
            "Morgan Manager",
            "too risky",
            null);

        Assert.Equal(ApprovalCaseStatus.Rejected, approval.Status);
        Assert.Equal(-1, approval.CurrentStageOrder);
        Assert.Throws<DomainException>(() =>
            approval.RecordDecision(
                ApprovalDecisionOutcome.Approved,
                "user-dd-001",
                [EntitleXRoles.DealDeskManager],
                "Dana",
                null,
                null));
    }

    [Fact]
    public void RecordDecision_RenewalRep_CannotApprove()
    {
        var approval = ApprovalCase.Open(
            RequestId.New(),
            60,
            ApprovalBand.RenewalManagerOnly);

        Assert.Throws<UnauthorizedApprovalTransitionException>(() =>
            approval.RecordDecision(
                ApprovalDecisionOutcome.Approved,
                "user-rep-001",
                [EntitleXRoles.RenewalRepresentative],
                "Riley Rep",
                null,
                null));
    }
}
