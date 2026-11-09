using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Application.Notifications;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.UnitTests.Notifications;

public sealed class ExpirationNotificationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-15T12:00:00Z");

    [Fact]
    public async Task Run_Dedupes_PerNotificationWindow_ForSameEntitlement()
    {
        var entitlement = new NormalizedEntitlementDto(
            "cs-ent-1",
            "CloudShield",
            "CUST-1001",
            "CS-EDGE",
            "Edge",
            "CloudShield Secure Edge",
            Now.AddDays(10),
            Now);

        var client = new FakeProvider("CloudShield", [entitlement]);
        var store = new InMemoryExpirationStore();
        var mail = new TrackingMail();
        var audit = new InMemoryAudit();

        var service = CreateService(client, store, mail, audit);

        var first = await service.RunAsync(["CloudShield"], Now);
        Assert.Equal(1, first.NotificationsSent);
        Assert.Single(mail.Warnings);
        Assert.Contains(audit.Events, e => e.EventType == AuditEventTypes.ExpirationNotificationSent);

        var second = await service.RunAsync(["CloudShield"], Now.AddHours(2));
        Assert.Equal(0, second.NotificationsSent);
        Assert.Equal(1, second.SkippedIdempotent);
        Assert.Single(mail.Warnings);
    }

    [Fact]
    public async Task Run_UsesProviderSpecificWarningThresholds()
    {
        var soon = new NormalizedEntitlementDto(
            "ng-1",
            "NetworkGuard",
            "CUST-1001",
            "NG-FW",
            "FW",
            "Firewall",
            Now.AddDays(40),
            Now);

        // CloudShield threshold=30 → should skip (40 days out).
        // NetworkGuard threshold=45 → should notify.
        var cloud = new FakeProvider("CloudShield", [
            soon with { Provider = "CloudShield", EntitlementId = "cs-far" }
        ]);
        var network = new FakeProvider("NetworkGuard", [soon]);

        var store = new InMemoryExpirationStore();
        var mail = new TrackingMail();
        var service = new ExpirationNotificationService(
            [cloud, network],
            store,
            mail,
            new InMemoryAudit(),
            new FakeUnitOfWork(),
            Options.Create(new ExpirationNotificationOptions
            {
                CloudShieldWarningDays = 30,
                NetworkGuardWarningDays = 45,
                CustomerWatchlist = ["CUST-1001"]
            }),
            Options.Create(new EmailPortalOptions { AppBaseUrl = "http://localhost:5173" }),
            NullLogger<ExpirationNotificationService>.Instance);

        var cloudResult = await service.RunAsync(["CloudShield"], Now);
        Assert.Equal(0, cloudResult.NotificationsSent);

        var ngResult = await service.RunAsync(["NetworkGuard"], Now);
        Assert.Equal(1, ngResult.NotificationsSent);
        Assert.Contains("NetworkGuard", mail.Warnings[0].PortalLink);
    }

    [Fact]
    public void NotificationWindowKeys_StableWithinDailyCadence()
    {
        var a = NotificationWindowKeys.ForExpiration(
            "CloudShield",
            "ent-1",
            Now,
            TimeSpan.FromHours(24));
        var b = NotificationWindowKeys.ForExpiration(
            "CloudShield",
            "ent-1",
            Now.AddHours(6),
            TimeSpan.FromHours(24));
        var nextDay = NotificationWindowKeys.ForExpiration(
            "CloudShield",
            "ent-1",
            Now.AddDays(1),
            TimeSpan.FromHours(24));

        Assert.Equal(a, b);
        Assert.NotEqual(a, nextDay);
    }

    private static ExpirationNotificationService CreateService(
        IProviderEntitlementClient client,
        InMemoryExpirationStore store,
        TrackingMail mail,
        InMemoryAudit audit) =>
        new(
            [client],
            store,
            mail,
            audit,
            new FakeUnitOfWork(),
            Options.Create(new ExpirationNotificationOptions
            {
                CloudShieldWarningDays = 30,
                NetworkGuardWarningDays = 45,
                CustomerWatchlist = ["CUST-1001"]
            }),
            Options.Create(new EmailPortalOptions { AppBaseUrl = "http://localhost:5173" }),
            NullLogger<ExpirationNotificationService>.Instance);

    private sealed class FakeProvider(
        string name,
        IReadOnlyList<NormalizedEntitlementDto> entitlements) : IProviderEntitlementClient
    {
        public string ProviderName => name;

        public Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
            string customerId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NormalizedEntitlementDto>>(
                entitlements.Where(e => e.CustomerId == customerId).ToList());

        public Task<NormalizedEntitlementDto?> GetByEntitlementIdAsync(
            string entitlementId,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entitlements.FirstOrDefault(e => e.EntitlementId == entitlementId));

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class InMemoryExpirationStore : IExpirationNotificationStore
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<bool> WasNotifiedAsync(
            string windowKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_keys.Contains(windowKey));

        public Task<bool> TryRecordAsync(
            ExpirationNotificationRecordDto record,
            CancellationToken cancellationToken = default)
        {
            if (!_keys.Add(record.WindowKey))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
    }

    private sealed class TrackingMail : IApprovalNotificationService
    {
        public List<(NormalizedEntitlementDto Entitlement, string PortalLink)> Warnings { get; } = [];

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
            CancellationToken cancellationToken = default)
        {
            Warnings.Add((entitlement, portalLink));
            return Task.CompletedTask;
        }
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
            Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);

        public Task<IReadOnlyList<AuditEventRecord>> ListByRequestKeysetAsync(
            RequestId requestId,
            int take,
            DateTimeOffset? afterOccurredAtUtc,
            Guid? afterId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEventRecord>>([]);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
