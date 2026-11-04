using System.Collections.Concurrent;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Reconciliation;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntitleX.Application.UnitTests.Reconciliation;

public sealed class ReconciliationServiceTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01Z");

    [Fact]
    public async Task Reconcile_WhenProviderApplied_RepairsLocalSuccess_Idempotently()
    {
        var (request, saga) = CreateUncertainRequest();
        var logical = request.Items[0].LogicalOperationId;
        var provider = new FakeProviderClient(
            ProviderSystem.CloudShield,
            logical,
            ProviderOperationLookupKind.ConfirmedApplied);

        var local = new InMemoryLocalOutcomes();
        var audit = new InMemoryAudit();
        var service = CreateService(request, saga, provider, local, audit);

        var first = await service.ReconcileRequestAsync(request.Id, "tester");
        var second = await service.ReconcileRequestAsync(request.Id, "tester");

        Assert.Equal(ReconciliationItemAction.RepairedSuccess, first.Items[0].Action);
        Assert.Equal(SagaState.Completed, saga.State);
        Assert.False(first.StillRequiresReconciliation);
        Assert.Single(local.ByLogical); // unique LogicalOperationId — never double-applied locally
        Assert.Contains(audit.Events, e => e.EventType == AuditEventTypes.ReconciliationItemRepairedSuccess);

        // Second pass finds no uncertain/pending candidates.
        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task Reconcile_WhenProviderFailed_MarksRetryEligible()
    {
        var (request, saga) = CreateUncertainRequest();
        var logical = request.Items[0].LogicalOperationId;
        var provider = new FakeProviderClient(
            ProviderSystem.CloudShield,
            logical,
            ProviderOperationLookupKind.ConfirmedFailed,
            failureReason: "PROVIDER_REJECTED");

        var service = CreateService(request, saga, provider, new InMemoryLocalOutcomes(), new InMemoryAudit());
        var result = await service.ReconcileRequestAsync(request.Id, "tester");

        Assert.Equal(ReconciliationItemAction.MarkedFailedRetryEligible, result.Items[0].Action);
        Assert.Equal(SagaState.Failed, saga.State);
        Assert.False(result.StillRequiresReconciliation);
        Assert.Equal(
            SagaItemOutcomeStatus.Failed,
            saga.ItemOutcomes.Single().Status);
    }

    [Fact]
    public async Task Reconcile_WhenProviderNotFound_KeepsUncertain_NoBlindRetry()
    {
        var (request, saga) = CreateUncertainRequest();
        var logical = request.Items[0].LogicalOperationId;
        var provider = new FakeProviderClient(
            ProviderSystem.CloudShield,
            logical,
            ProviderOperationLookupKind.NotFound);

        var audit = new InMemoryAudit();
        var service = CreateService(request, saga, provider, new InMemoryLocalOutcomes(), audit);
        var result = await service.ReconcileRequestAsync(request.Id, "tester");

        Assert.Equal(ReconciliationItemAction.KeptUnknown, result.Items[0].Action);
        Assert.True(result.StillRequiresReconciliation);
        Assert.Equal(SagaState.ReconciliationRequired, saga.State);
        Assert.Equal(SagaItemOutcomeStatus.Uncertain, saga.ItemOutcomes.Single().Status);
        Assert.Contains(audit.Events, e => e.EventType == AuditEventTypes.ReconciliationItemStillUnknown);
        Assert.Equal(0, provider.MutationAttempts); // read-only probe — never POSTs
    }

    [Fact]
    public async Task Reconcile_WhenProviderUnknown_NeverBlindRetries()
    {
        var (request, saga) = CreateUncertainRequest();
        var logical = request.Items[0].LogicalOperationId;
        var provider = new FakeProviderClient(
            ProviderSystem.CloudShield,
            logical,
            ProviderOperationLookupKind.Unknown);

        var service = CreateService(request, saga, provider, new InMemoryLocalOutcomes(), new InMemoryAudit());
        var result = await service.ReconcileRequestAsync(request.Id, "tester");

        Assert.Equal(ReconciliationItemAction.KeptUnknown, result.Items[0].Action);
        Assert.True(result.StillRequiresReconciliation);
        Assert.Equal(0, provider.MutationAttempts);
    }

    [Fact]
    public async Task Reconcile_LocalFailedProviderApplied_CorrectsMismatch()
    {
        var (request, saga, failedOp) = CreatePartialWithFailedAndSucceeded();
        var provider = new FakeProviderClient(
            ProviderSystem.CloudShield,
            failedOp,
            ProviderOperationLookupKind.ConfirmedApplied);

        var local = new InMemoryLocalOutcomes();
        local.ByLogical[failedOp.Value] = new LocalFulfillmentOutcome(
            request.Id.Value,
            request.Items.Single(i => i.LogicalOperationId.Equals(failedOp)).Id.Value,
            failedOp.Value,
            "ent-fail",
            "CloudShield",
            Succeeded: true,
            null,
            null,
            DateTimeOffset.UtcNow);

        var service = CreateService(request, saga, provider, local, new InMemoryAudit());
        var result = await service.ReconcileRequestAsync(request.Id, "tester");

        Assert.Contains(result.Items, i => i.Action == ReconciliationItemAction.RepairedSuccess);
        Assert.Equal(
            SagaItemOutcomeStatus.Succeeded,
            saga.ItemOutcomes.Single(o => o.LogicalOperationId.Equals(failedOp)).Status);
        Assert.Equal(SagaState.Completed, saga.State);
    }

    [Fact]
    public async Task ConcurrentReconcile_SameLogicalOp_RecordsLocalOutcomeOnce()
    {
        var (request, saga) = CreateUncertainRequest();
        var logical = request.Items[0].LogicalOperationId;
        var provider = new FakeProviderClient(
            ProviderSystem.CloudShield,
            logical,
            ProviderOperationLookupKind.ConfirmedApplied);
        var local = new InMemoryLocalOutcomes();
        var service = CreateService(request, saga, provider, local, new InMemoryAudit());

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                service.ReconcileRequestAsync(request.Id, "tester")));

        Assert.Single(local.ByLogical);
        Assert.Equal(SagaState.Completed, saga.State);
        Assert.Equal(SagaItemOutcomeStatus.Succeeded, saga.ItemOutcomes.Single().Status);
    }

    private static ReconciliationService CreateService(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        IProviderOperationStatusClient provider,
        InMemoryLocalOutcomes local,
        InMemoryAudit audit) =>
        new(
            new FakeSagaRepository(saga),
            new FakeRequestRepository(request),
            new NoopOrchestrator(),
            [provider],
            local,
            audit,
            new FakeUnitOfWork(),
            NullLogger<ReconciliationService>.Instance);

    private static (LicenseExtensionRequest Request, LicenseExtensionSaga Saga) CreateUncertainRequest()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-RECON-1",
            "rep@entitlex.local",
            [(EntitlementId.From("ent-1"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.ApplyTimeout(DateTimeOffset.UtcNow.AddHours(7));
        return (request, saga);
    }

    private static (LicenseExtensionRequest Request, LicenseExtensionSaga Saga, LogicalOperationId FailedOp)
        CreatePartialWithFailedAndSucceeded()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-RECON-2",
            "rep@entitlex.local",
            [
                (EntitlementId.From("ent-ok"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30)),
                (EntitlementId.From("ent-fail"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))
            ]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(request.Items[0].LogicalOperationId);
        saga.RecordItemFailed(request.Items[1].LogicalOperationId, "E", "fail");
        return (request, saga, request.Items[1].LogicalOperationId);
    }

    private sealed class FakeProviderClient : IProviderOperationStatusClient
    {
        private readonly LogicalOperationId _logical;
        private readonly ProviderOperationLookupKind _kind;
        private readonly string? _failureReason;

        public FakeProviderClient(
            ProviderSystem provider,
            LogicalOperationId logical,
            ProviderOperationLookupKind kind,
            string? failureReason = null)
        {
            Provider = provider;
            _logical = logical;
            _kind = kind;
            _failureReason = failureReason;
        }

        public ProviderSystem Provider { get; }

        public int MutationAttempts { get; private set; }

        public Task<ProviderOperationStatus> GetByLogicalOperationIdAsync(
            LogicalOperationId logicalOperationId,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(_logical, logicalOperationId);
            return Task.FromResult(new ProviderOperationStatus(
                Provider,
                logicalOperationId,
                _kind,
                ProviderStatus: _kind.ToString(),
                NewExpiryUtc: Baseline.AddDays(30),
                FailureReason: _failureReason,
                Detail: $"fake:{_kind}"));
        }
    }

    private sealed class InMemoryLocalOutcomes : ILocalFulfillmentOutcomeStore
    {
        public ConcurrentDictionary<Guid, LocalFulfillmentOutcome> ByLogical { get; } = new();

        public Task<LocalFulfillmentOutcome?> FindByLogicalOperationIdAsync(
            LogicalOperationId logicalOperationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ByLogical.TryGetValue(logicalOperationId.Value, out var row) ? row : null);

        public Task<IReadOnlyList<LocalFulfillmentOutcome>> ListByRequestAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalFulfillmentOutcome>>(
                ByLogical.Values.Where(v => v.RequestId == requestId.Value).ToList());

        public Task<bool> TryRecordAsync(
            LocalFulfillmentOutcome outcome,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ByLogical.TryAdd(outcome.LogicalOperationId, outcome));
    }

    private sealed class InMemoryAudit : IAuditEventStore
    {
        public ConcurrentBag<AuditEventRecord> Events { get; } = [];

        public Task AppendAsync(AuditEventRecord record, CancellationToken cancellationToken = default)
        {
            Events.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListByRequestAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEventRecord>>(
                Events.Where(e => e.RequestId?.Equals(requestId) == true).ToList());
    }

    private sealed class FakeSagaRepository(LicenseExtensionSaga saga) : ILicenseExtensionSagaRepository
    {
        public Task AddAsync(LicenseExtensionSaga s, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionSaga?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LicenseExtensionSaga?>(
                saga.RequestId.Equals(requestId) ? saga : null);

        public Task<LicenseExtensionSaga?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(saga.Id == id ? saga : null);

        public Task<IReadOnlyList<LicenseExtensionSaga>> ListTimedOutAsync(
            DateTimeOffset nowUtc,
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LicenseExtensionSaga>>([]);

        public Task<IReadOnlyList<LicenseExtensionSaga>> ListRequiringReconciliationAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LicenseExtensionSaga>>([saga]);
    }

    private sealed class FakeRequestRepository(LicenseExtensionRequest request)
        : ILicenseExtensionRequestRepository
    {
        public Task AddAsync(LicenseExtensionRequest r, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(request.Id.Equals(id) ? request : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class NoopOrchestrator : ILicenseExtensionSagaOrchestrator
    {
        public Task<LicenseExtensionSaga> StartForRequestAsync(
            LicenseExtensionRequest request,
            ApprovalBand band,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task OnApprovalDecisionAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalDecision decision,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnRejectedWithoutDecisionAsync(
            LicenseExtensionRequest request,
            string reason,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DispatchFulfillmentAsync(
            LicenseExtensionRequest request,
            Guid? correlationId = null,
            Guid? causationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnFulfillmentDispatchedAsync(
            RequestId requestId,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnItemCompletedAsync(
            RequestId requestId,
            LogicalOperationId logicalOperationId,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnItemFailedAsync(
            RequestId requestId,
            LogicalOperationId logicalOperationId,
            string? errorCode,
            string? errorMessage,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ApplyElapsedTimeoutsAsync(
            DateTimeOffset nowUtc,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
