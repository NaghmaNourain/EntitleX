using EntitleX.Application.Abstractions;
using EntitleX.Application.Authorization;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.Insights;
using EntitleX.Application.Insights.Abstractions;
using EntitleX.Application.Insights.ExplainRequest;
using EntitleX.Application.Insights.Models;
using EntitleX.Application.Insights.ReconcileRca;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using EntitleX.SharedKernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntitleX.Application.UnitTests.Insights;

public sealed class RequestInsightHandlerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01Z");

    [Fact]
    public async Task Explain_UsesFallback_WhenCompletionDisabled()
    {
        var request = CreateRequest("owner-1");
        var builder = new StubBuilder(new RequestInsightContext
        {
            RequestId = request.Id.Value,
            Request = request,
            Approval = null,
            Saga = null,
            Items = [],
            PoisonOutbox = [],
            RecentAudit = [],
            BuilderNotes = []
        });
        var handler = new ExplainRequestQueryHandler(
            new ResourceAuthorizationService(
                new FakeCurrentUser("owner-1", [EntitleXRoles.RenewalRepresentative]),
                new FakeRequestRepository(request)),
            builder,
            new DeterministicInsightComposer(),
            CreateDisabledNarrator());

        var dto = await handler.Handle(
            new ExplainRequestQuery(request.Id.Value),
            CancellationToken.None);

        Assert.Equal("Fallback", dto.NarrationMode);
        Assert.Equal("Explain", dto.InsightKind);
        Assert.Null(dto.Diagnosis);
        Assert.False(string.IsNullOrWhiteSpace(dto.Summary));
    }

    [Fact]
    public async Task Rca_NonAdmin_Forbidden()
    {
        var handler = new ReconcileRcaQueryHandler(
            new FakeCurrentUser("rep", [EntitleXRoles.RenewalRepresentative]),
            new StubBuilder(new RequestInsightContext { RequestId = Guid.NewGuid() }),
            new DeterministicInsightComposer(),
            CreateDisabledNarrator());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new ReconcileRcaQuery(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Rca_PlatformAdmin_Ok_Fallback()
    {
        var request = CreateRequest("owner-1");
        var context = new RequestInsightContext
        {
            RequestId = request.Id.Value,
            Request = request,
            Items = [],
            PoisonOutbox = [],
            RecentAudit = [],
            BuilderNotes = []
        };
        var handler = new ReconcileRcaQueryHandler(
            new FakeCurrentUser("admin", [EntitleXRoles.PlatformAdmin]),
            new StubBuilder(context),
            new DeterministicInsightComposer(),
            CreateDisabledNarrator());

        var dto = await handler.Handle(
            new ReconcileRcaQuery(request.Id.Value),
            CancellationToken.None);

        Assert.Equal("Rca", dto.InsightKind);
        Assert.Equal("Fallback", dto.NarrationMode);
        Assert.Null(dto.Diagnosis);
    }

    private static RequestInsightNarrator CreateDisabledNarrator() =>
        new(
            new DisabledGate(),
            Array.Empty<Microsoft.Extensions.AI.IChatClient>(),
            new IncidentDiagnosisValidator(),
            NullLogger<RequestInsightNarrator>.Instance);

    private static LicenseExtensionRequest CreateRequest(string requestedBy) =>
        LicenseExtensionRequest.CreateFresh(
            $"EXT-{Guid.NewGuid():N}",
            requestedBy,
            [
                (
                    EntitlementId.From("ent-1"),
                    ProviderSystem.CloudShield,
                    Baseline,
                    Baseline.AddDays(14))
            ]);

    private sealed class StubBuilder(RequestInsightContext context) : IRequestInsightContextBuilder
    {
        public Task<RequestInsightContext> BuildAsync(
            RequestId requestId,
            bool includeLiveProbes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(context);
    }

    private sealed class DisabledGate : IAiAssistGate
    {
        public bool IsEnabled => false;
        public string? Model => null;
        public float Temperature => 0;
    }

    private sealed class FakeCurrentUser(string userId, string[] roles, string[]? customerIds = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => userId;
        public string DisplayName => userId;
        public string? Email => $"{userId}@test.local";
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyList<string> CustomerIds => customerIds ?? [];
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeRequestRepository(LicenseExtensionRequest request)
        : ILicenseExtensionRequestRepository
    {
        public Task AddAsync(LicenseExtensionRequest entity, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(id.Value == request.Id.Value ? request : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
