using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.DecideApproval;
using EntitleX.Application.Common.Exceptions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.UnitTests.Approval;

public sealed class DecideApprovalCommandHandlerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task Handle_WhenActorLacksStageRole_ThrowsForbidden()
    {
        var (request, approval, saga) = CreatePendingRmCase(days: 60);
        var orchestrator = new FakeSagaOrchestrator(saga);
        var handler = new DecideApprovalCommandHandler(
            new FakeRequestRepository(request),
            new FakeApprovalRepository(approval),
            orchestrator,
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
        Assert.Equal(0, orchestrator.DispatchCount);
    }

    [Fact]
    public async Task Handle_WhenRenewalManagerApproves_DispatchesFulfillmentViaSaga()
    {
        var (request, approval, saga) = CreatePendingRmCase(days: 60);
        var orchestrator = new FakeSagaOrchestrator(saga);
        var handler = new DecideApprovalCommandHandler(
            new FakeRequestRepository(request),
            new FakeApprovalRepository(approval),
            orchestrator,
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
        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment, request.Status);
        Assert.Equal(1, request.ApprovalVersion);
        Assert.False(string.IsNullOrWhiteSpace(request.ApprovedScopeHash));
        Assert.Equal(1, orchestrator.DispatchCount);
        Assert.Equal(SagaState.QueuedForFulfillment, saga.State);
    }

    private static (LicenseExtensionRequest Request, ApprovalCase Approval, LicenseExtensionSaga Saga)
        CreatePendingRmCase(int days)
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

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.RenewalManagerOnly);
        return (request, approval, saga);
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

        public Task<IReadOnlyList<ApprovalCase>> ListPendingAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalCase>>(
                approval.Status == ApprovalCaseStatus.Pending ? [approval] : []);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private sealed class FakeCurrentUser(string userId, string name, string[] roles, string[]? customerIds = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => userId;
        public string DisplayName => name;
        public string? Email => $"{userId}@entitlex.local";
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyList<string> CustomerIds => customerIds ?? [];
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

        public Task NotifyApprovalReminderAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalStage stage,
            TimeSpan pendingFor,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NotifyExpirationWarningAsync(
            EntitleX.Application.EntitlementAggregation.Models.NormalizedEntitlementDto entitlement,
            string portalLink,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSagaOrchestrator(LicenseExtensionSaga saga) : ILicenseExtensionSagaOrchestrator
    {
        public int DispatchCount { get; private set; }

        public Task<LicenseExtensionSaga> StartForRequestAsync(
            LicenseExtensionRequest request,
            ApprovalBand band,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(saga);

        public Task OnApprovalDecisionAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalDecision decision,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (decision.Outcome == ApprovalDecisionOutcome.Rejected)
            {
                saga.RecordRejection(decision.ActorUserId, correlationId, decision.Comment);
                return Task.CompletedTask;
            }

            if (decision.StageRole == ApprovalStageRole.RenewalManager)
            {
                saga.RecordRenewalManagerApproval(decision.ActorUserId, correlationId);
            }
            else if (decision.StageRole == ApprovalStageRole.DealDeskManager)
            {
                saga.RecordDealDeskApproval(decision.ActorUserId, correlationId);
            }

            return Task.CompletedTask;
        }

        public Task OnRejectedWithoutDecisionAsync(
            LicenseExtensionRequest request,
            string reason,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;


        public Task DispatchFulfillmentAsync(
            LicenseExtensionRequest request,
            LicenseExtensionSaga saga,
            Guid? correlationId = null,
            Guid? causationId = null,
            CancellationToken cancellationToken = default) =>
            DispatchFulfillmentAsync(request, correlationId, causationId, cancellationToken);

        public Task DispatchFulfillmentAsync(
            LicenseExtensionRequest request,
            Guid? correlationId = null,
            Guid? causationId = null,
            CancellationToken cancellationToken = default)
        {
            request.BeginFulfillment();
            saga.QueueFulfillment(request.Items.Select(i => (
                i.Id,
                i.EntitlementId,
                i.LogicalOperationId)));
            DispatchCount++;
            return Task.CompletedTask;
        }

        public Task OnFulfillmentDispatchedAsync(
            RequestId requestId,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OnItemCompletedAsync(
            RequestId requestId,
            LogicalOperationId logicalOperationId,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OnItemFailedAsync(
            RequestId requestId,
            LogicalOperationId logicalOperationId,
            string? errorCode,
            string? errorMessage,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyElapsedTimeoutsAsync(
            DateTimeOffset nowUtc,
            int take = 50,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
