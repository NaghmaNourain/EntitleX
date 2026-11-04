using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Application.Notifications;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.UnitTests.Notifications;

public sealed class ApprovalReminderServiceTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-02-01T00:00:00Z");

    [Fact]
    public async Task Run_SendsReminderOnce_ThenEscalatesAfterConfiguredDelay()
    {
        var (request, approval, saga) = CreatePending(T0);
        var store = new InMemoryReminderStore();
        var mail = new TrackingMail();
        var audit = new InMemoryAudit();
        var options = Options.Create(new ApprovalReminderOptions
        {
            PendingThresholdDays = 7,
            EscalationDelayHours = 24,
            TerminalSagaState = "Escalated"
        });

        var service = new ApprovalReminderService(
            new FixedApprovals(approval),
            new FixedRequests(request),
            new FixedSagas(saga),
            store,
            mail,
            audit,
            new FakeUnitOfWork(),
            options,
            NullLogger<ApprovalReminderService>.Instance);

        var day7 = await service.RunAsync(T0.AddDays(7));
        Assert.Equal(1, day7.RemindersSent);
        Assert.Equal(0, day7.EscalationsApplied);
        Assert.Single(mail.Reminders);
        Assert.Single(store.ByWindow);

        // Same window — idempotent (no second reminder).
        var day7Again = await service.RunAsync(T0.AddDays(7).AddHours(1));
        Assert.Equal(0, day7Again.RemindersSent);
        Assert.Single(mail.Reminders);

        var day8 = await service.RunAsync(T0.AddDays(7).AddHours(24));
        Assert.Equal(1, day8.EscalationsApplied);
        Assert.Equal(SagaState.Escalated, saga.State);
        Assert.Contains(audit.Events, e => e.EventType == AuditEventTypes.ApprovalReminderEscalated);

        // Escalation window also idempotent.
        var day8Again = await service.RunAsync(T0.AddDays(8));
        Assert.Equal(0, day8Again.EscalationsApplied);
    }

    [Fact]
    public async Task Run_UsesConfiguredTerminalState_NotHardcodedLiteralInServicePath()
    {
        var (request, approval, saga) = CreatePending(T0);
        var options = Options.Create(new ApprovalReminderOptions
        {
            PendingThresholdDays = 7,
            EscalationDelayHours = 24,
            TerminalSagaState = nameof(SagaState.Escalated)
        });

        var store = new InMemoryReminderStore();
        var service = new ApprovalReminderService(
            new FixedApprovals(approval),
            new FixedRequests(request),
            new FixedSagas(saga),
            store,
            new TrackingMail(),
            new InMemoryAudit(),
            new FakeUnitOfWork(),
            options,
            NullLogger<ApprovalReminderService>.Instance);

        await service.RunAsync(T0.AddDays(7));
        await service.RunAsync(T0.AddDays(7).AddHours(24));

        Assert.Equal(options.Value.TerminalSagaState, store.ByWindow.Values.Single().TerminalStateApplied);
    }

    private static (LicenseExtensionRequest Request, ApprovalCase Approval, LicenseExtensionSaga Saga) CreatePending(
        DateTimeOffset createdAt)
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-REM-1",
            "rep-1",
            [
                (
                    EntitlementId.From("ent-1"),
                    ProviderSystem.CloudShield,
                    createdAt.AddDays(30),
                    createdAt.AddDays(90))
            ],
            createdAtUtc: createdAt);
        request.Submit();
        request.MarkPendingApproval();

        var approval = ApprovalCase.Open(
            request.Id,
            maxExtensionDays: 60,
            ApprovalBand.RenewalManagerOnly,
            createdAt);

        var saga = LicenseExtensionSaga.Start(
            request.Id,
            ApprovalBand.RenewalManagerOnly,
            createdAtUtc: createdAt);

        return (request, approval, saga);
    }

    private sealed class FixedApprovals(ApprovalCase approval) : IApprovalCaseRepository
    {
        public Task AddAsync(ApprovalCase approvalCase, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ApprovalCase?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalCase?>(approval.RequestId == requestId ? approval : null);

        public Task<ApprovalCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalCase?>(approval.Id == id ? approval : null);

        public Task<IReadOnlyList<ApprovalCase>> ListPendingAsync(
            int take,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApprovalCase>>(
                approval.Status == ApprovalCaseStatus.Pending ? [approval] : []);
    }

    private sealed class FixedRequests(LicenseExtensionRequest request) : ILicenseExtensionRequestRepository
    {
        public Task AddAsync(LicenseExtensionRequest req, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LicenseExtensionRequest?>(request.Id == id ? request : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FixedSagas(LicenseExtensionSaga saga) : ILicenseExtensionSagaRepository
    {
        public Task AddAsync(LicenseExtensionSaga s, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionSaga?> GetByRequestIdAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LicenseExtensionSaga?>(saga.RequestId == requestId ? saga : null);

        public Task<LicenseExtensionSaga?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LicenseExtensionSaga?>(saga.Id == id ? saga : null);

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

    private sealed class InMemoryReminderStore : IApprovalReminderStore
    {
        public Dictionary<string, ApprovalReminderRecordDto> ByWindow { get; } = new(StringComparer.Ordinal);

        public Task<ApprovalReminderRecordDto?> GetByWindowKeyAsync(
            string windowKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ByWindow.TryGetValue(windowKey, out var r) ? r : null);

        public Task<ApprovalReminderRecordDto?> GetForStageAsync(
            Guid approvalCaseId,
            int stageOrder,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ByWindow.Values.FirstOrDefault(r =>
                r.ApprovalCaseId == approvalCaseId && r.StageOrder == stageOrder));

        public Task<bool> TryRecordReminderAsync(
            ApprovalReminderRecordDto record,
            CancellationToken cancellationToken = default)
        {
            if (ByWindow.ContainsKey(record.WindowKey))
            {
                return Task.FromResult(false);
            }

            ByWindow[record.WindowKey] = record;
            return Task.FromResult(true);
        }

        public Task MarkEscalatedAsync(
            string windowKey,
            string terminalStateApplied,
            DateTimeOffset escalatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (ByWindow.TryGetValue(windowKey, out var existing))
            {
                ByWindow[windowKey] = existing with
                {
                    EscalatedAtUtc = escalatedAtUtc,
                    TerminalStateApplied = terminalStateApplied
                };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TrackingMail : IApprovalNotificationService
    {
        public List<(LicenseExtensionRequest Request, ApprovalStage Stage)> Reminders { get; } = [];

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
            CancellationToken cancellationToken = default)
        {
            Reminders.Add((request, stage));
            return Task.CompletedTask;
        }

        public Task NotifyExpirationWarningAsync(
            NormalizedEntitlementDto entitlement,
            string portalLink,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InMemoryAudit : IAuditEventStore
    {
        public List<AuditEventRecord> Events { get; } = [];

        public Task AppendAsync(AuditEventRecord record, CancellationToken cancellationToken = default)
        {
            Events.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEventRecord>> ListByRequestAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEventRecord>>(
                Events.Where(e => e.RequestId == requestId).ToList());
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
