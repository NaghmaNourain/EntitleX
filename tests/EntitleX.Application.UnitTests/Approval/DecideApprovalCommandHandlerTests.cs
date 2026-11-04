using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.DecideApproval;
using EntitleX.Application.Common.Exceptions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.UnitTests.Approval;

public sealed class DecideApprovalCommandHandlerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task Handle_WhenActorLacksStageRole_ThrowsForbidden()
    {
        var (request, approval) = CreatePendingRmCase(days: 60);
        var handler = new DecideApprovalCommandHandler(
            new FakeRequestRepository(request),
            new FakeApprovalRepository(approval),
            new FakeUnitOfWork(),
            new FakeCurrentUser(
                "user-dd-001",
                "Dana",
                [EntitleXRoles.DealDeskManager]),
            new NoopNotifications());

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new DecideApprovalCommand(request.Id.Value, "Approved", "nope"),
                CancellationToken.None));

        Assert.Contains("RenewalManager", ex.Message, StringComparison.Ordinal);
        Assert.Equal(LicenseExtensionRequestStatus.PendingApproval, request.Status);
        Assert.Equal(ApprovalCaseStatus.Pending, approval.Status);
    }

    [Fact]
    public async Task Handle_WhenRenewalManagerApproves_UpdatesRequestScope()
    {
        var (request, approval) = CreatePendingRmCase(days: 60);
        var handler = new DecideApprovalCommandHandler(
            new FakeRequestRepository(request),
            new FakeApprovalRepository(approval),
            new FakeUnitOfWork(),
            new FakeCurrentUser(
                "user-rm-001",
                "Morgan",
                [EntitleXRoles.RenewalManager]),
            new NoopNotifications());

        var dto = await handler.Handle(
            new DecideApprovalCommand(request.Id.Value, "Approved", "lgtm", "corr-9"),
            CancellationToken.None);

        Assert.Equal(nameof(ApprovalCaseStatus.Approved), dto.Status);
        Assert.Equal(LicenseExtensionRequestStatus.Approved, request.Status);
        Assert.Equal(1, request.ApprovalVersion);
        Assert.False(string.IsNullOrWhiteSpace(request.ApprovedScopeHash));
    }

    private static (LicenseExtensionRequest Request, ApprovalCase Approval) CreatePendingRmCase(int days)
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-APP-1",
            "rep@entitlex.local",
            [
                (
                    EntitlementId.From("ent-1"),
                    ProviderSystem.CloudShield,
                    Baseline,
                    Baseline.AddDays(days))
            ]);
        request.Submit();
        request.MarkPendingApproval();

        var approval = ApprovalCase.Open(
            request.Id,
            days,
            ApprovalBand.RenewalManagerOnly);

        return (request, approval);
    }

    private sealed class FakeRequestRepository(LicenseExtensionRequest request)
        : ILicenseExtensionRequestRepository
    {
        public Task AddAsync(LicenseExtensionRequest r, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LicenseExtensionRequest?>(id.Equals(request.Id) ? request : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeApprovalRepository(ApprovalCase approval) : IApprovalCaseRepository
    {
        public Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ApprovalCase?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalCase?>(
                requestId.Equals(approval.RequestId) ? approval : null);

        public Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalCase?>(approval.Id == id ? approval : null);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private sealed class FakeCurrentUser(string userId, string name, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => userId;
        public string DisplayName => name;
        public string? Email => $"{userId}@entitlex.local";
        public IReadOnlyCollection<string> Roles => roles;
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class NoopNotifications : IApprovalNotificationService
    {
        public Task NotifyApprovalStartedAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NotifyStageReadyAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalStage stage,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NotifyDecisionRecordedAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalDecision decision,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
