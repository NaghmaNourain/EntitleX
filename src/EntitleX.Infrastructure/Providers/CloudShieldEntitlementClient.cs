using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace EntitleX.Infrastructure.Providers;

public sealed class CloudShieldEntitlementClient : IProviderEntitlementClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudShieldEntitlementClient> _logger;

    public CloudShieldEntitlementClient(
        IHttpClientFactory httpClientFactory,
        ILogger<CloudShieldEntitlementClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ProviderName => "CloudShield";

    public async Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
        string customerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        CorrelationContext.Current = correlationId;
        var client = _httpClientFactory.CreateClient(ProviderHttpClientNames.CloudShield);

        _logger.LogDebug(
            "CloudShield discovery GET for {CustomerId}. CorrelationId={CorrelationId}",
            customerId,
            correlationId);

        using var response = await client.GetAsync(
            $"api/v1/accounts/{Uri.EscapeDataString(customerId)}/entitlements",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<CloudShieldEntitlementResponse>>(
            cancellationToken: cancellationToken)
            ?? [];

        return payload.Select(e => new NormalizedEntitlementDto(
            e.EntitlementKey,
            ProviderName,
            e.CustomerAccountId,
            e.ProductCode,
            e.ProductGroup,
            e.DisplayName,
            e.ExpiresAtUtc,
            e.UpdatedAtUtc)).ToList();
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ProviderHttpClientNames.CloudShield);
        using var response = await client.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private sealed record CloudShieldEntitlementResponse(
        [property: JsonPropertyName("entitlementKey")] string EntitlementKey,
        [property: JsonPropertyName("customerAccountId")] string CustomerAccountId,
        [property: JsonPropertyName("productCode")] string ProductCode,
        [property: JsonPropertyName("productGroup")] string? ProductGroup,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
        [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);
}
