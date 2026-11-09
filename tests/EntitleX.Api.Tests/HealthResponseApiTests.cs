using System.Net;
using System.Text.Json;

namespace EntitleX.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class HealthResponseApiTests : IAsyncLifetime
{
    private readonly EntitleXApiFactory _factory;
    private HttpClient? _client;

    public HealthResponseApiTests(EntitleXApiFactory factory)
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
    public async Task Ready_ReturnsStatusAndCheckNamesOnly()
    {
        var response = await _client!.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
        Assert.True(doc.RootElement.TryGetProperty("checks", out var checks));
        Assert.Equal(JsonValueKind.Array, checks.ValueKind);

        foreach (var check in checks.EnumerateArray())
        {
            Assert.True(check.TryGetProperty("name", out _));
            Assert.True(check.TryGetProperty("status", out _));
            Assert.False(check.TryGetProperty("description", out _));
            Assert.False(check.TryGetProperty("exception", out _));
            Assert.False(check.TryGetProperty("duration", out _));
            Assert.False(check.TryGetProperty("data", out _));
        }
    }

    [DockerFact]
    public async Task Providers_ReturnsStatusAndCheckNamesOnly()
    {
        var response = await _client!.GetAsync("/health/providers");
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
        Assert.True(doc.RootElement.TryGetProperty("checks", out var checks));

        foreach (var check in checks.EnumerateArray())
        {
            Assert.True(check.TryGetProperty("name", out _));
            Assert.True(check.TryGetProperty("status", out _));
            Assert.False(check.TryGetProperty("description", out _));
        }
    }
}
