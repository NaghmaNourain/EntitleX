using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Persistence;
using EntitleX.SharedKernel.Ids;
using Microsoft.Extensions.DependencyInjection;

namespace EntitleX.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ConcurrencyConflictApiTests : IAsyncLifetime
{
    private readonly EntitleXApiFactory _factory;
    private HttpClient? _client;

    public ConcurrencyConflictApiTests(EntitleXApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        if (!TestPostgres.IsConfigured)
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
    public async Task Create_DuplicateExtensionNumber_Returns409()
    {
        await SeedRequestAsync("EXT-DUP-409", "user-rep-001");

        var token = await MintAsync(EntitleXRoles.RenewalRepresentative);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/license-extension-requests/")
        {
            Content = JsonContent.Create(new
            {
                extensionNumber = "EXT-DUP-409",
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
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [DockerFact]
    public async Task StartApproval_WhenCaseExists_Returns409()
    {
        var requestId = await SeedRequestWithApprovalAsync();
        var token = await MintAsync(EntitleXRoles.RenewalRepresentative);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/license-extension-requests/{requestId}/approval/start");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

    private async Task SeedRequestAsync(string extensionNumber, string owner)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();
        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var aggregate = LicenseExtensionRequest.CreateFresh(
            extensionNumber,
            owner,
            [
                (
                    EntitlementId.From("cs-ent-dup"),
                    ProviderSystem.CloudShield,
                    baseline,
                    baseline.AddMonths(3))
            ]);
        aggregate.Submit();
        db.LicenseExtensionRequests.Add(aggregate);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedRequestWithApprovalAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();
        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var aggregate = LicenseExtensionRequest.CreateFresh(
            "EXT-APPR-409",
            "user-rep-001",
            [
                (
                    EntitlementId.From("cs-ent-appr"),
                    ProviderSystem.CloudShield,
                    baseline,
                    baseline.AddMonths(3))
            ]);
        aggregate.Submit();
        aggregate.MarkPendingApproval();

        var maxDays = ApprovalRules.ComputeMaxExtensionDays(aggregate.Items);
        var approval = ApprovalCase.Open(
            aggregate.Id,
            maxDays,
            ApprovalRules.EvaluateBand(maxDays));

        db.LicenseExtensionRequests.Add(aggregate);
        db.ApprovalCases.Add(approval);
        await db.SaveChangesAsync();
        return aggregate.Id.Value;
    }
}
