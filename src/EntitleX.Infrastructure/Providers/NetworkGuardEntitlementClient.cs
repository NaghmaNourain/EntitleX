using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace EntitleX.Infrastructure.Providers;

public sealed class NetworkGuardEntitlementClient : IProviderEntitlementClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NetworkGuardEntitlementClient> _logger;

    public NetworkGuardEntitlementClient(
        IHttpClientFactory httpClientFactory,
        ILogger<NetworkGuardEntitlementClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string ProviderName => "NetworkGuard";

    public async Task<IReadOnlyList<NormalizedEntitlementDto>> DiscoverByCustomerAsync(
        string customerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        CorrelationContext.Current = correlationId;
        var client = _httpClientFactory.CreateClient(ProviderHttpClientNames.NetworkGuard);

        _logger.LogDebug(
            "NetworkGuard discovery GET for {CustomerId}. CorrelationId={CorrelationId}",
            customerId,
            correlationId);

        using var response = await client.GetAsync(
            $"api/v1/tenants/{Uri.EscapeDataString(customerId)}/licenses",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<NetworkGuardLicenseResponse>>(
            cancellationToken: cancellationToken)
            ?? [];

        return payload.Select(e => new NormalizedEntitlementDto(
            e.LicenseId,
            ProviderName,
            e.TenantExternalId,
            e.SkuCode,
            e.LicenseGroup,
            e.DisplayName,
            e.ValidUntilUtc,
            e.UpdatedAtUtc)).ToList();
    }

    public async Task<NormalizedEntitlementDto?> GetByEntitlementIdAsync(
        string entitlementId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        CorrelationContext.Current = correlationId;
        var client = _httpClientFactory.CreateClient(ProviderHttpClientNames.NetworkGuard);

        using var response = await client.GetAsync(
            $"api/v1/licenses/{Uri.EscapeDataString(entitlementId)}",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var e = await response.Content.ReadFromJsonAsync<NetworkGuardLicenseResponse>(
            cancellationToken: cancellationToken);
        if (e is null)
        {
            return null;
        }

        return new NormalizedEntitlementDto(
            e.LicenseId,
            ProviderName,
            e.TenantExternalId,
            e.SkuCode,
            e.LicenseGroup,
            e.DisplayName,
            e.ValidUntilUtc,
            e.UpdatedAtUtc);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ProviderHttpClientNames.NetworkGuard);
        using var response = await client.GetAsync("health", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private sealed record NetworkGuardLicenseResponse(
        [property: JsonPropertyName("licenseId")] string LicenseId,
        [property: JsonPropertyName("tenantExternalId")] string TenantExternalId,
        [property: JsonPropertyName("skuCode")] string SkuCode,
        [property: JsonPropertyName("licenseGroup")] string? LicenseGroup,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("validUntilUtc")] DateTimeOffset ValidUntilUtc,
        [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc);
}
