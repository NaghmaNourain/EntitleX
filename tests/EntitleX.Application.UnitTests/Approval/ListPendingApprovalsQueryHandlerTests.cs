using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.ListPendingApprovals;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;

namespace EntitleX.Application.UnitTests.Approval;

public sealed class ListPendingApprovalsQueryHandlerTests
{
    [Fact]
    public async Task Spoofed_role_filter_is_ignored_for_renewal_manager()
    {
        var rmCase = ApprovalCase.Open(RequestId.New(), 45, ApprovalBand.RenewalManagerOnly);
        var ddCase = ApprovalCase.Open(RequestId.New(), 120, ApprovalBand.RenewalManagerThenDealDesk);
        // Advance DD case past RM so current stage is DealDesk.
        ddCase.RecordDecision(
            ApprovalDecisionOutcome.Approved,
            "mgr",
            [EntitleXRoles.RenewalManager],
            "Manager",
            null,
            null);

        var handler = new ListPendingApprovalsQueryHandler(
            new FakeApprovalRepository([rmCase, ddCase]),
            new FakeCurrentUser("mgr-1", [EntitleXRoles.RenewalManager]));

        var page = await handler.Handle(
            new ListPendingApprovalsQuery(Role: EntitleXRoles.DealDeskManager, Skip: 0, Take: 50),
            CancellationToken.None);

        Assert.All(page.Items, i => Assert.Equal(EntitleXRoles.RenewalManager, i.CurrentStageRole));
        Assert.DoesNotContain(page.Items, i => i.CurrentStageRole == EntitleXRoles.DealDeskManager);
    }

    [Fact]
    public async Task Admin_may_filter_to_deal_desk_queue()
    {
        var rmCase = ApprovalCase.Open(RequestId.New(), 45, ApprovalBand.RenewalManagerOnly);
        var ddCase = ApprovalCase.Open(RequestId.New(), 120, ApprovalBand.RenewalManagerThenDealDesk);
        ddCase.RecordDecision(
            ApprovalDecisionOutcome.Approved,
            "mgr",
            [EntitleXRoles.RenewalManager],
            "Manager",
            null,
            null);

        var handler = new ListPendingApprovalsQueryHandler(
            new FakeApprovalRepository([rmCase, ddCase]),
            new FakeCurrentUser("admin", [EntitleXRoles.PlatformAdmin]));

        var page = await handler.Handle(
            new ListPendingApprovalsQuery(Role: EntitleXRoles.DealDeskManager, Skip: 0, Take: 50),
            CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(EntitleXRoles.DealDeskManager, page.Items[0].CurrentStageRole);
    }

    [Fact]
    public async Task Representative_without_stage_role_sees_empty_queue()
    {
        var rmCase = ApprovalCase.Open(RequestId.New(), 45, ApprovalBand.RenewalManagerOnly);
        var handler = new ListPendingApprovalsQueryHandler(
            new FakeApprovalRepository([rmCase]),
            new FakeCurrentUser("rep-1", [EntitleXRoles.RenewalRepresentative]));

        var page = await handler.Handle(
            new ListPendingApprovalsQuery(Role: EntitleXRoles.RenewalManager, Skip: 0, Take: 50),
            CancellationToken.None);

        Assert.Empty(page.Items);
    }

    private sealed class FakeApprovalRepository(IReadOnlyList<ApprovalCase> cases) : IApprovalCaseRepository
    {
        public Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ApprovalCase?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(cases.FirstOrDefault(c => c.RequestId.Equals(requestId)));

        public Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(cases.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<ApprovalCase>> ListPendingAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalCase>>(
                cases.Where(c => c.Status == ApprovalCaseStatus.Pending).Take(take).ToList());
    }

    private sealed class FakeCurrentUser(string userId, string[] roles, string[]? customerIds = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => userId;
        public string DisplayName => userId;
        public string? Email => $"{userId}@entitlex.local";
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyList<string> CustomerIds => customerIds ?? [];
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
