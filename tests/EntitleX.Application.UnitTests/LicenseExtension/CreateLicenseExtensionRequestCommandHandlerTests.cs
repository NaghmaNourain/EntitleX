using EntitleX.Application.Abstractions;
using EntitleX.Application.Authorization;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Application.Fulfillment;
using EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;
using EntitleX.Application.Saga;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

using EntitleX.Messaging.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.UnitTests.LicenseExtension;

/// <summary>
/// Regression: auto-approve create used to call DispatchFulfillmentAsync after
/// StartForRequestAsync AddAsync, but RequireSagaAsync re-queried the repository.
/// EF Core (and this blind fake) does not return Added-but-unsaved entities, which
/// threw InvalidOperationException "Saga for request was not found".
/// Also covers H3: RequestedBy is bound from ICurrentUser, not the client body.
/// </summary>
public sealed class CreateLicenseExtensionRequestCommandHandlerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    [Fact]
    public async Task Handle_AutoApprove_DispatchesFulfillment_WhenSagaNotYetSaved()
    {
        var requests = new FakeRequestRepository();
        var approvals = new FakeApprovalRepository();
        var sagas = new UnsavedBlindSagaRepository();
        var outbox = new CapturingOutboxWriter();
        var unitOfWork = new CapturingUnitOfWork();
        var notifications = new NoopNotifications();
        var currentUser = new FakeCurrentUser(
            "user-rep-001",
            "Riley Rep",
            [EntitleXRoles.RenewalRepresentative],
            ["CUST-1"]);

        var orchestrator = CreateOrchestrator(sagas, requests, outbox);

        var handler = new CreateLicenseExtensionRequestCommandHandler(
            requests,
            approvals,
            orchestrator,
            unitOfWork,
            notifications,
            currentUser,
            new ResourceAuthorizationService(currentUser, requests),
            [new FakeEntitlementClient("CloudShield", Baseline)]);

        var dto = await handler.Handle(
            new CreateLicenseExtensionRequestCommand(
                ExtensionNumber: null,
                Items:
                [
                    new CreateLicenseExtensionRequestItemDto(
                        "ent-cs-1",
                        "CloudShield",
                        Baseline.AddDays(-90), // client lie — server uses provider expiry
                        Baseline.AddDays(14))
                ]),
            CancellationToken.None);

        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment.ToString(), dto.Status);
        Assert.Equal(Baseline, requests.Store.Values.Single().Items[0].CurrentExpiryUtc);
        Assert.Equal("user-rep-001", dto.RequestedBy);
        Assert.NotNull(outbox.LastEnvelope);
        Assert.Equal(MessageTypes.ProviderLicenseExtensionCommand, outbox.LastEnvelope!.MessageType);
        Assert.Equal(1, unitOfWork.SaveCount);

        var persistedSaga = sagas.Pending.Single();
        Assert.Equal(dto.Id, persistedSaga.RequestId.Value);
        Assert.Equal(SagaState.Processing, persistedSaga.State);
        Assert.Equal(ApprovalBand.AutoApprove, persistedSaga.ApprovalBand);
    }

    [Fact]
    public async Task Handle_BindsRequestedBy_FromCurrentUser_IgnoringClient()
    {
        var requests = new FakeRequestRepository();
        var approvals = new FakeApprovalRepository();
        var sagas = new UnsavedBlindSagaRepository();
        var outbox = new CapturingOutboxWriter();
        var currentUser = new FakeCurrentUser(
            "user-bound-42",
            "Bound Actor",
            [EntitleXRoles.RenewalRepresentative],
            ["CUST-1"]);

        var handler = new CreateLicenseExtensionRequestCommandHandler(
            requests,
            approvals,
            CreateOrchestrator(sagas, requests, outbox),
            new CapturingUnitOfWork(),
            new NoopNotifications(),
            currentUser,
            new ResourceAuthorizationService(currentUser, requests),
            [new FakeEntitlementClient("CloudShield", Baseline)]);

        var dto = await handler.Handle(
            new CreateLicenseExtensionRequestCommand(
                "EXT-ACTOR-1",
                [
                    new CreateLicenseExtensionRequestItemDto(
                        "ent-cs-1",
                        "CloudShield",
                        Baseline,
                        Baseline.AddDays(14))
                ]),
            CancellationToken.None);

        Assert.Equal("user-bound-42", dto.RequestedBy);
        Assert.Equal("user-bound-42", requests.Store.Values.Single().RequestedBy);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ThrowsForbidden()
    {
        var unauth = new FakeCurrentUser(string.Empty, string.Empty, [], authenticated: false);
        var requests = new FakeRequestRepository();
        var handler = new CreateLicenseExtensionRequestCommandHandler(
            requests,
            new FakeApprovalRepository(),
            CreateOrchestrator(new UnsavedBlindSagaRepository(), requests, new CapturingOutboxWriter()),
            new CapturingUnitOfWork(),
            new NoopNotifications(),
            unauth,
            new ResourceAuthorizationService(unauth, requests),
            [new FakeEntitlementClient("CloudShield", Baseline)]);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new CreateLicenseExtensionRequestCommand(
                    null,
                    [
                        new CreateLicenseExtensionRequestItemDto(
                            "ent-cs-1",
                            "CloudShield",
                            Baseline,
                            Baseline.AddDays(14))
                    ]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_HumanApprovalBand_StartsSagaWithoutDispatch()
    {
        var requests = new FakeRequestRepository();
        var approvals = new FakeApprovalRepository();
        var sagas = new UnsavedBlindSagaRepository();
        var outbox = new CapturingOutboxWriter();
        var unitOfWork = new CapturingUnitOfWork();

        var orchestrator = CreateOrchestrator(sagas, requests, outbox);

        var currentUser = new FakeCurrentUser(
            "user-rep-001",
            "Riley Rep",
            [EntitleXRoles.RenewalRepresentative],
            ["CUST-1"]);
        var handler = new CreateLicenseExtensionRequestCommandHandler(
            requests,
            approvals,
            orchestrator,
            unitOfWork,
            new NoopNotifications(),
            currentUser,
            new ResourceAuthorizationService(currentUser, requests),
            [new FakeEntitlementClient("CloudShield", Baseline)]);

        var dto = await handler.Handle(
            new CreateLicenseExtensionRequestCommand(
                "EXT-HUMAN-1",
                [
                    new CreateLicenseExtensionRequestItemDto(
                        "ent-cs-1",
                        "CloudShield",
                        Baseline,
                        Baseline.AddDays(45))
                ]),
            CancellationToken.None);

        Assert.Equal(LicenseExtensionRequestStatus.PendingApproval.ToString(), dto.Status);
        Assert.Null(outbox.LastEnvelope);
        Assert.Equal(SagaState.PendingRenewalManagerApproval, sagas.Pending.Single().State);
    }

    private static LicenseExtensionSagaOrchestrator CreateOrchestrator(
        ILicenseExtensionSagaRepository sagas,
        ILicenseExtensionRequestRepository requests,
        IOutboxWriter outbox) =>
        new(
            sagas,
            requests,
            new FulfillmentOutboxService(
                outbox,
                Options.Create(new FulfillmentEnqueueOptions
                {
                    FulfillmentQueueName = "entitlex-fulfillment"
                })),
            new LoggingSagaTimeoutHook(NullLogger<LoggingSagaTimeoutHook>.Instance),
            NullLogger<LicenseExtensionSagaOrchestrator>.Instance);

    private sealed class UnsavedBlindSagaRepository : ILicenseExtensionSagaRepository
    {
        private readonly List<LicenseExtensionSaga> _pending = [];
        private readonly List<LicenseExtensionSaga> _saved = [];

        public IReadOnlyList<LicenseExtensionSaga> Pending => _pending;

        public Task AddAsync(LicenseExtensionSaga saga, CancellationToken cancellationToken = default)
        {
            _pending.Add(saga);
            return Task.CompletedTask;
        }

        public Task<LicenseExtensionSaga?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.FirstOrDefault(s => s.RequestId.Equals(requestId)));

        public Task<LicenseExtensionSaga?> GetByRequestIdReadOnlyAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            GetByRequestIdAsync(requestId, cancellationToken);

        public Task<LicenseExtensionSaga?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<LicenseExtensionSaga>> ListTimedOutAsync(
            DateTimeOffset nowUtc,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LicenseExtensionSaga>>([]);

        public Task<IReadOnlyList<LicenseExtensionSaga>> ListRequiringReconciliationAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LicenseExtensionSaga>>([]);
    }

    private sealed class FakeRequestRepository : ILicenseExtensionRequestRepository
    {
        public Dictionary<Guid, LicenseExtensionRequest> Store { get; } = new();

        public Task AddAsync(LicenseExtensionRequest request, CancellationToken cancellationToken = default)
        {
            Store[request.Id.Value] = request;
            return Task.CompletedTask;
        }

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.TryGetValue(id.Value, out var r) ? r : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.Values.Any(r => r.ExtensionNumber == extensionNumber));
    }

    private sealed class FakeApprovalRepository : IApprovalCaseRepository
    {
        public Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ApprovalCase?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalCase?>(null);

        public Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalCase?>(null);

        public Task<IReadOnlyList<ApprovalCase>> ListPendingAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalCase>>([]);
    }

    private sealed class CapturingOutboxWriter : IOutboxWriter
    {
        public MessageEnvelope? LastEnvelope { get; private set; }

        public Task EnqueueAsync(
            MessageEnvelope envelope,
            OutboxDispatchTarget target,
            CancellationToken cancellationToken = default)
        {
            LastEnvelope = envelope;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCurrentUser(
        string userId,
        string name,
        string[] roles,
        string[]? customerIds = null,
        bool authenticated = true) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string UserId => userId;
        public string DisplayName => name;
        public string? Email => string.IsNullOrEmpty(userId) ? null : $"{userId}@entitlex.local";
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyList<string> CustomerIds => customerIds ?? [];
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeEntitlementClient(string providerName, DateTimeOffset expiry)
        : IProviderEntitlementClient
    {
        public string ProviderName => providerName;

        public Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
            string customerId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NormalizedEntitlementDto>>([]);

        public Task<NormalizedEntitlementDto?> GetByEntitlementIdAsync(
            string entitlementId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NormalizedEntitlementDto?>(new NormalizedEntitlementDto(
                entitlementId,
                providerName,
                "CUST-1",
                "SKU",
                null,
                entitlementId,
                expiry,
                null));

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
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
            NormalizedEntitlementDto entitlement,
            string portalLink,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
