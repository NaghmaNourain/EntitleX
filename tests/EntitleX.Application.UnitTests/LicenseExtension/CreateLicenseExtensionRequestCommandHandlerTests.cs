using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Application.Fulfillment;
using EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;
using EntitleX.Application.Saga;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.UnitTests.LicenseExtension;

/// <summary>
/// Regression: auto-approve create used to call DispatchFulfillmentAsync after
/// StartForRequestAsync AddAsync, but RequireSagaAsync re-queried the repository.
/// EF Core (and this blind fake) does not return Added-but-unsaved entities, which
/// threw InvalidOperationException "Saga for request was not found".
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

        var orchestrator = CreateOrchestrator(sagas, requests, outbox);

        var handler = new CreateLicenseExtensionRequestCommandHandler(
            requests,
            approvals,
            orchestrator,
            unitOfWork,
            notifications);

        var dto = await handler.Handle(
            new CreateLicenseExtensionRequestCommand(
                "user-rep-001",
                ExtensionNumber: null,
                Items:
                [
                    new CreateLicenseExtensionRequestItemDto(
                        "ent-cs-1",
                        "CloudShield",
                        Baseline,
                        Baseline.AddDays(14))
                ]),
            CancellationToken.None);

        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment.ToString(), dto.Status);
        Assert.NotNull(outbox.LastEnvelope);
        Assert.Equal(MessageTypes.FulfillmentCommand, outbox.LastEnvelope!.MessageType);
        Assert.Equal(1, unitOfWork.SaveCount);

        var persistedSaga = sagas.Pending.Single();
        Assert.Equal(dto.Id, persistedSaga.RequestId.Value);
        Assert.Equal(SagaState.QueuedForFulfillment, persistedSaga.State);
        Assert.Equal(ApprovalBand.AutoApprove, persistedSaga.ApprovalBand);
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

        var handler = new CreateLicenseExtensionRequestCommandHandler(
            requests,
            approvals,
            orchestrator,
            unitOfWork,
            new NoopNotifications());

        var dto = await handler.Handle(
            new CreateLicenseExtensionRequestCommand(
                "user-rep-001",
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
                Options.Create(new MessagingOptions
                {
                    Sqs = { FulfillmentQueueName = "entitlex-fulfillment" }
                })),
            new LoggingSagaTimeoutHook(NullLogger<LoggingSagaTimeoutHook>.Instance),
            NullLogger<LicenseExtensionSagaOrchestrator>.Instance);

    /// <summary>
    /// Mimics EF Core: GetByRequestIdAsync only returns entities that were SaveChanges'd,
    /// not those sitting in the change tracker as Added.
    /// </summary>
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
        private readonly Dictionary<Guid, LicenseExtensionRequest> _store = new();

        public Task AddAsync(LicenseExtensionRequest request, CancellationToken cancellationToken = default)
        {
            _store[request.Id.Value] = request;
            return Task.CompletedTask;
        }

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id.Value, out var r) ? r : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Values.Any(r => r.ExtensionNumber == extensionNumber));
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
