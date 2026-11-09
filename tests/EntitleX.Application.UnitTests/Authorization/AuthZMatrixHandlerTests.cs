using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.ListPendingApprovals;
using EntitleX.Application.Authorization;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.LicenseExtension.GetLicenseExtensionRequest;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.UnitTests.Authorization;

/// <summary>
/// Handler-level AuthZ matrix (M4). Mirrors the intended WebApplicationFactory
/// checks without requiring Microsoft.AspNetCore.Mvc.Testing.
/// </summary>
public sealed class AuthZMatrixHandlerTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    [Fact]
    public async Task GetRequest_NonOwnerRepresentative_Forbidden()
    {
        var request = CreateRequest("owner-1");
        var handler = new GetLicenseExtensionRequestQueryHandler(
            new FakeRequestRepository(request),
            new ResourceAuthorizationService(
                new FakeCurrentUser("other-rep", [EntitleXRoles.RenewalRepresentative]),
                new FakeRequestRepository(request)));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetLicenseExtensionRequestQuery(request.Id.Value), CancellationToken.None));
    }

    [Fact]
    public async Task GetRequest_OwnerRepresentative_Ok()
    {
        var request = CreateRequest("owner-1");
        var handler = new GetLicenseExtensionRequestQueryHandler(
            new FakeRequestRepository(request),
            new ResourceAuthorizationService(
                new FakeCurrentUser("owner-1", [EntitleXRoles.RenewalRepresentative]),
                new FakeRequestRepository(request)));

        var dto = await handler.Handle(
            new GetLicenseExtensionRequestQuery(request.Id.Value),
            CancellationToken.None);
        Assert.Equal(request.Id.Value, dto.Id);
    }

    [Theory]
    [InlineData(EntitleXRoles.RenewalManager)]
    [InlineData(EntitleXRoles.DealDeskManager)]
    [InlineData(EntitleXRoles.PlatformAdmin)]
    public async Task GetRequest_Privileged_Ok(string role)
    {
        var request = CreateRequest("owner-1");
        var handler = new GetLicenseExtensionRequestQueryHandler(
            new FakeRequestRepository(request),
            new ResourceAuthorizationService(
                new FakeCurrentUser("mgr", [role]),
                new FakeRequestRepository(request)));

        var dto = await handler.Handle(
            new GetLicenseExtensionRequestQuery(request.Id.Value),
            CancellationToken.None);
        Assert.Equal(request.Id.Value, dto.Id);
    }

    [Fact]
    public async Task ListPending_DealDesk_CannotSpoofRenewalManagerQueue()
    {
        var rmCase = ApprovalCase.Open(RequestId.New(), 45, ApprovalBand.RenewalManagerOnly);
        var handler = new ListPendingApprovalsQueryHandler(
            new FakeApprovals([rmCase]),
            new FakeCurrentUser("dd", [EntitleXRoles.DealDeskManager]));

        var page = await handler.Handle(
            new ListPendingApprovalsQuery(Role: EntitleXRoles.RenewalManager),
            CancellationToken.None);

        Assert.Empty(page.Items);
    }

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

    private sealed class FakeRequestRepository(LicenseExtensionRequest request) : ILicenseExtensionRequestRepository
    {
        public Task AddAsync(LicenseExtensionRequest r, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(id.Equals(request.Id) ? request : null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeApprovals(IReadOnlyList<ApprovalCase> cases) : IApprovalCaseRepository
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
}
