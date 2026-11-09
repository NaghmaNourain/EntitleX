using EntitleX.Application.Abstractions;
using EntitleX.Application.Authorization;
using EntitleX.Application.Common.Exceptions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.UnitTests.Authorization;

public sealed class ResourceAuthorizationServiceTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    [Fact]
    public void EnsureCanReadRequest_Owner_Allows()
    {
        var request = CreateRequest("user-rep-001");
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("user-rep-001", [EntitleXRoles.RenewalRepresentative]),
            new FakeRequestRepository());

        sut.EnsureCanReadRequest(request);
    }

    [Fact]
    public void EnsureCanReadRequest_NonOwnerRepresentative_Denies()
    {
        var request = CreateRequest("user-other");
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("user-rep-001", [EntitleXRoles.RenewalRepresentative]),
            new FakeRequestRepository());

        Assert.Throws<ForbiddenException>(() => sut.EnsureCanReadRequest(request));
    }

    [Theory]
    [InlineData(EntitleXRoles.PlatformAdmin)]
    [InlineData(EntitleXRoles.DealDeskManager)]
    [InlineData(EntitleXRoles.RenewalManager)]
    public void EnsureCanReadRequest_PrivilegedRoles_AllowAny(string role)
    {
        var request = CreateRequest("user-other");
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("privileged-user", [role]),
            new FakeRequestRepository());

        sut.EnsureCanReadRequest(request);
    }

    [Fact]
    public async Task EnsureCanReadRequestAsync_Missing_ThrowsNotFound()
    {
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("user-rep-001", [EntitleXRoles.RenewalRepresentative]),
            new FakeRequestRepository());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.EnsureCanReadRequestAsync(RequestId.New(), CancellationToken.None));
    }

    [Fact]
    public void EnsureCanSearchEntitlements_PlatformAdmin_Allows()
    {
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("admin", [EntitleXRoles.PlatformAdmin]),
            new FakeRequestRepository());

        sut.EnsureCanSearchEntitlements();
    }

    [Fact]
    public void EnsureCanSearchEntitlements_DealDesk_Allows()
    {
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("dd", [EntitleXRoles.DealDeskManager]),
            new FakeRequestRepository());

        sut.EnsureCanSearchEntitlements();
    }

    [Theory]
    [InlineData(EntitleXRoles.RenewalRepresentative)]
    [InlineData(EntitleXRoles.RenewalManager)]
    public void EnsureCanSearchEntitlements_OtherRoles_Deny(string role)
    {
        var sut = new ResourceAuthorizationService(
            new FakeCurrentUser("user", [role]),
            new FakeRequestRepository());

        Assert.Throws<ForbiddenException>(() => sut.EnsureCanSearchEntitlements());
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

    private sealed class FakeCurrentUser(string userId, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => userId;
        public string DisplayName => userId;
        public string? Email => $"{userId}@entitlex.local";
        public IReadOnlyCollection<string> Roles => roles;
        public bool IsInRole(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeRequestRepository : ILicenseExtensionRequestRepository
    {
        public Task AddAsync(LicenseExtensionRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<LicenseExtensionRequest?> GetByIdAsync(
            RequestId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LicenseExtensionRequest?>(null);

        public Task<bool> ExtensionNumberExistsAsync(
            string extensionNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
