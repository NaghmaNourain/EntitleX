using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Identity;
using EntitleX.Infrastructure.Persistence;
using EntitleX.SharedKernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace EntitleX.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthZApiTests : IAsyncLifetime
{
    private readonly EntitleXApiFactory _factory;
    private HttpClient? _client;

    public AuthZApiTests(EntitleXApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        await _factory.ResetDatabaseAsync();
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [DockerFact]
    public async Task ProtectedEndpoint_Unauthenticated_Returns401()
    {
        var response = await _client!.GetAsync($"/api/v1/license-extension-requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DockerFact]
    public async Task Create_WrongRole_Returns403()
    {
        var token = await MintAsync(EntitleXRoles.RenewalManager);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/license-extension-requests/")
        {
            Content = JsonContent.Create(new
            {
                extensionNumber = "EXT-AUTHZ-403",
                items = new[]
                {
                    new
                    {
                        entitlementId = "cs-ent-1",
                        provider = "CloudShield",
                        currentExpiryUtc = "2026-12-01T00:00:00Z",
                        requestedExpiryUtc = "2027-01-01T00:00:00Z"
                    }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task GetRequest_NonOwnerRepresentative_Returns403()
    {
        var requestId = await SeedOwnedRequestAsync("owner-rep");
        var token = await MintAsync(EntitleXRoles.RenewalRepresentative); // user-rep-001

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/license-extension-requests/{requestId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task PendingApprovals_AcceptsPageEnvelope()
    {
        var token = await MintAsync(EntitleXRoles.RenewalManager);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/approvals/pending?page=1&pageSize=10&role=RenewalManager");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.True(doc.RootElement.TryGetProperty("totalCount", out var totalCount));
        Assert.True(doc.RootElement.TryGetProperty("page", out _));
        Assert.True(doc.RootElement.TryGetProperty("pageSize", out _));
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.Equal(JsonValueKind.Number, totalCount.ValueKind);
    }

    private async Task<string> MintAsync(string role)
    {
        var response = await _client!.PostAsJsonAsync(
            "/api/v1/dev/tokens",
            new { role, customerIds = new[] { "CUST-1" } });
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("Missing accessToken");
    }

    private async Task<Guid> SeedOwnedRequestAsync(string ownerUserId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();
        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var aggregate = LicenseExtensionRequest.CreateFresh(
            "EXT-OWNER-DENY",
            ownerUserId,
            [
                (
                    EntitlementId.From("cs-ent-deny"),
                    ProviderSystem.CloudShield,
                    baseline,
                    baseline.AddMonths(6))
            ]);
        aggregate.Submit();
        db.LicenseExtensionRequests.Add(aggregate);
        await db.SaveChangesAsync();
        return aggregate.Id.Value;
    }
}
