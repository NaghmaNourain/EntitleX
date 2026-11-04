using System.Collections.Concurrent;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.LicenseExtension.Retry;
using EntitleX.Application.LicenseExtension.RetryFailed;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.UnitTests.LicenseExtension;

public sealed class RetryFailedCommandHandlerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    [Fact]
    public async Task Handle_ReusesApproval_WhenScopeHashStillValid()
    {
        var (parent, saga) = CreatePartiallyFulfilledSource();
        var claimStore = new InMemoryRetryClaimStore();
        var requests = new FakeRequestRepository(parent);
        var orchestrator = new RecordingSagaOrchestrator();
        var handler = CreateHandler(parent, saga, claimStore, requests, orchestrator);

        var dto = await handler.Handle(
            new RetryFailedLicenseExtensionsCommand(
                parent.Id.Value,
                "idem-reuse-1",
                Notes: "please retry"),
            CancellationToken.None);

        Assert.Equal(parent.Id.Value, dto.ParentId);
        Assert.Single(dto.Items);
        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment.ToString(), dto.Status);
        Assert.Equal(1, dto.ApprovalVersion);
        Assert.False(string.IsNullOrWhiteSpace(dto.ApprovedScopeHash));
        Assert.Equal(1, orchestrator.DispatchCount);
        Assert.Single(parent.RetryHistory);

        var retry = requests.Added.Single();
        Assert.NotEqual(parent.Items[1].AttemptId, retry.Items[0].AttemptId);
        Assert.Equal(parent.Items[1].LogicalOperationId, retry.Items[0].LogicalOperationId);
    }

    [Fact]
    public async Task Handle_LostResponse_ReturnsPriorRetry_SameIdempotencyKey()
    {
        var (parent, saga) = CreatePartiallyFulfilledSource();
        var claimStore = new InMemoryRetryClaimStore();
        var requests = new FakeRequestRepository(parent);
        var handler = CreateHandler(parent, saga, claimStore, requests, new RecordingSagaOrchestrator());

        var first = await handler.Handle(
            new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-lost-1"),
            CancellationToken.None);

        var second = await handler.Handle(
            new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-lost-1"),
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(requests.Added);
    }

    [Fact]
    public async Task Handle_WhenUncertainOutcomes_BlocksViaReconciliationGate()
    {
        var (parent, saga) = CreatePartiallyFulfilledSource();
        // Force uncertain: rebuild saga in ReconciliationRequired with uncertain item.
        var uncertainSaga = LicenseExtensionSaga.Start(parent.Id, ApprovalBand.AutoApprove);
        uncertainSaga.QueueFulfillment(parent.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        uncertainSaga.MarkProcessing();
        uncertainSaga.ApplyTimeout(DateTimeOffset.UtcNow.AddHours(7));

        var handler = CreateHandler(
            parent,
            uncertainSaga,
            new InMemoryRetryClaimStore(),
            new FakeRequestRepository(parent),
            new RecordingSagaOrchestrator());

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(
                new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-uncert-1"),
                CancellationToken.None));

        Assert.Contains("reconcil", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WhenCallerNotRepresentative_Forbidden()
    {
        var (parent, saga) = CreatePartiallyFulfilledSource();
        var handler = CreateHandler(
            parent,
            saga,
            new InMemoryRetryClaimStore(),
            new FakeRequestRepository(parent),
            new RecordingSagaOrchestrator(),
            roles: [EntitleXRoles.RenewalManager]);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-forbid-1"),
                CancellationToken.None));
    }

    [Fact]
    public async Task DualRetryCalls_SameSourceItems_OneWinsOneConflicts()
    {
        var (parent, saga) = CreatePartiallyFulfilledSource();
        var claimStore = new InMemoryRetryClaimStore();
        var requests = new FakeRequestRepository(parent);
        var handler = CreateHandler(parent, saga, claimStore, requests, new RecordingSagaOrchestrator());

        var results = new ConcurrentBag<(bool Ok, Guid? Id, string? Error)>();

        await Task.WhenAll(
            Task.Run(async () =>
            {
                try
                {
                    var dto = await handler.Handle(
                        new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-a"),
                        CancellationToken.None);
                    results.Add((true, dto.Id, null));
                }
                catch (ConflictException ex)
                {
                    results.Add((false, null, ex.Message));
                }
            }),
            Task.Run(async () =>
            {
                try
                {
                    var dto = await handler.Handle(
                        new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-b"),
                        CancellationToken.None);
                    results.Add((true, dto.Id, null));
                }
                catch (ConflictException ex)
                {
                    results.Add((false, null, ex.Message));
                }
            }));

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, results.Count(r => !r.Ok));
        Assert.Single(requests.Added);
    }

    [Fact]
    public async Task DualRetryCalls_SameIdempotencyKey_BothReturnSameRetry()
    {
        var (parent, saga) = CreatePartiallyFulfilledSource();
        var claimStore = new InMemoryRetryClaimStore();
        var requests = new FakeRequestRepository(parent);
        var handler = CreateHandler(parent, saga, claimStore, requests, new RecordingSagaOrchestrator());

        var results = new ConcurrentBag<Guid>();

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                var dto = await handler.Handle(
                    new RetryFailedLicenseExtensionsCommand(parent.Id.Value, "idem-shared"),
                    CancellationToken.None);
                results.Add(dto.Id);
            })));

        Assert.Equal(8, results.Count);
        Assert.Single(results.Distinct());
        Assert.Single(requests.Added);
    }

    private static RetryFailedLicenseExtensionsCommandHandler CreateHandler(
        LicenseExtensionRequest parent,
        LicenseExtensionSaga saga,
        IRetryClaimStore claimStore,
        FakeRequestRepository requests,
        RecordingSagaOrchestrator orchestrator,
        string[]? roles = null) =>
        new(
            requests,
            new FakeSagaRepository(saga),
            new FakeApprovalRepository(),
            orchestrator,
            new StubReconciliationGate(),
            claimStore,
            new FakeUnitOfWork(),
            new FakeCurrentUser("rep-1", "Rep One", roles ?? [EntitleXRoles.RenewalRepresentative]),
            new NoopNotifications());

    private static (LicenseExtensionRequest Parent, LicenseExtensionSaga Saga) CreatePartiallyFulfilledSource()
    {
        var parent = LicenseExtensionRequest.CreateFresh(
            "EXT-SRC",
            "rep@entitlex.local",
            [SampleItem("ent-1"), SampleItem("ent-2")]);
        parent.Submit();
        parent.MarkPendingApproval();
        parent.RecordApproval(ApprovedScopeHasher.Compute(parent));
        parent.BeginFulfillment();
        parent.CompleteFulfillment(1, 1);

        var saga = LicenseExtensionSaga.Start(parent.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(parent.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(parent.Items[0].LogicalOperationId);
        saga.RecordItemFailed(parent.Items[1].LogicalOperationId, "E", "fail");
        return (parent, saga);
    }

    private static (
        EntitlementId EntitlementId,
        ProviderSystem Provider,
        DateTimeOffset CurrentExpiryUtc,
        DateTimeOffset RequestedExpiryUtc) SampleItem(string id) =>
        (EntitlementId.From(id), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30));

    private sealed class InMemoryRetryClaimStore : IRetryClaimStore
    {
        private readonly ConcurrentDictionary<string, RetryIdempotencyLookup> _byKey = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _itemLocks = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public Task<RetryIdempotencyLookup?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_byKey.TryGetValue(idempotencyKey, out var found) ? found : null);

        public Task<bool> TryClaimItemsAsync(
            RequestId sourceRequestId,
            RequestId retryRequestId,
            string idempotencyKey,
            IReadOnlyList<LogicalOperationId> logicalOperationIds,
            string? notes,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_byKey.ContainsKey(idempotencyKey))
                {
                    return Task.FromResult(false);
                }

                foreach (var id in logicalOperationIds)
                {
                    var lockKey = $"{sourceRequestId.Value:D}:{id.Value:D}";
                    if (_itemLocks.ContainsKey(lockKey))
                    {
                        return Task.FromResult(false);
                    }
                }

                foreach (var id in logicalOperationIds)
                {
                    _itemLocks[$"{sourceRequestId.Value:D}:{id.Value:D}"] = 1;
                }

                _byKey[idempotencyKey] = new RetryIdempotencyLookup(
                    idempotencyKey,
                    sourceRequestId,
                    retryRequestId,
                    DateTimeOffset.UtcNow);
                return Task.FromResult(true);
            }
        }
    }

    private sealed class FakeRequestRepository : ILicenseExtensionRequestRepository
    {
        private readonly ConcurrentDictionary<Guid, LicenseExtensionRequest> _store = new();

        public FakeRequestRepository(LicenseExtensionRequest seed) =>
            _store[seed.Id.Value] = seed;

        public List<LicenseExtensionRequest> Added { get; } = [];

        public Task AddAsync(LicenseExtensionRequest request, CancellationToken cancellationToken = default)
        {
            _store[request.Id.Value] = request;
            lock (Added)
            {
                Added.Add(request);
            }

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

    private sealed class FakeSagaRepository(LicenseExtensionSaga saga) : ILicenseExtensionSagaRepository
    {
        private readonly ConcurrentDictionary<Guid, LicenseExtensionSaga> _byRequest = new()
        {
            [saga.RequestId.Value] = saga
        };

        public Task AddAsync(LicenseExtensionSaga s, CancellationToken cancellationToken = default)
        {
            _byRequest[s.RequestId.Value] = s;
            return Task.CompletedTask;
        }

        public Task<LicenseExtensionSaga?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_byRequest.TryGetValue(requestId.Value, out var s) ? s : null);

        public Task<LicenseExtensionSaga?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_byRequest.Values.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<LicenseExtensionSaga>> ListTimedOutAsync(
            DateTimeOffset nowUtc,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LicenseExtensionSaga>>([]);

        public Task<IReadOnlyList<LicenseExtensionSaga>> ListRequiringReconciliationAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LicenseExtensionSaga>>(
                _byRequest.Values
                    .Where(s => s.State == SagaState.ReconciliationRequired
                        || s.ItemOutcomes.Any(o => o.Status == SagaItemOutcomeStatus.Uncertain))
                    .Take(take)
                    .ToList());
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
    }

    private sealed class RecordingSagaOrchestrator : ILicenseExtensionSagaOrchestrator
    {
        private readonly ConcurrentDictionary<Guid, LicenseExtensionSaga> _sagas = new();
        private int _dispatch;

        public int DispatchCount => _dispatch;

        public Task<LicenseExtensionSaga> StartForRequestAsync(
            LicenseExtensionRequest request,
            ApprovalBand band,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            var saga = LicenseExtensionSaga.Start(request.Id, band, correlationId: correlationId);
            _sagas[request.Id.Value] = saga;
            return Task.FromResult(saga);
        }

        public Task OnApprovalDecisionAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalDecision decision,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OnRejectedWithoutDecisionAsync(
            LicenseExtensionRequest request,
            string reason,
            string? correlationId = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DispatchFulfillmentAsync(
            LicenseExtensionRequest request,
            Guid? correlationId = null,
            Guid? causationId = null,
            CancellationToken cancellationToken = default)
        {
            if (!_sagas.TryGetValue(request.Id.Value, out var saga))
            {
                saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
                _sagas[request.Id.Value] = saga;
            }

            request.BeginFulfillment();
            saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
            Interlocked.Increment(ref _dispatch);
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
