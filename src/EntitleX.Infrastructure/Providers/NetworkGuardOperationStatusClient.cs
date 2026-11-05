using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace EntitleX.Infrastructure.Providers;

public sealed class NetworkGuardOperationStatusClient : IProviderOperationStatusClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NetworkGuardOperationStatusClient> _logger;

    public NetworkGuardOperationStatusClient(
        IHttpClientFactory httpClientFactory,
        ILogger<NetworkGuardOperationStatusClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ProviderSystem Provider => ProviderSystem.NetworkGuard;

    public async Task<ProviderOperationStatus> GetByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            CorrelationContext.Current = correlationId;
        }

        var client = _httpClientFactory.CreateClient(ProviderHttpClientNames.NetworkGuard);

        try
        {
            using var response = await client.GetAsync(
                $"api/v1/operations/{logicalOperationId.Value:D}",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ProviderOperationStatus(
                    Provider,
                    logicalOperationId,
                    ProviderOperationLookupKind.NotFound,
                    ProviderStatus: null,
                    NewExpiryUtc: null,
                    FailureReason: null,
                    Detail: "NetworkGuard has no AppliedLicenseOperation for this LogicalOperationId");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "NetworkGuard operation lookup returned {StatusCode} for {LogicalOperationId}",
                    (int)response.StatusCode,
                    logicalOperationId.Value);

                return new ProviderOperationStatus(
                    Provider,
                    logicalOperationId,
                    ProviderOperationLookupKind.Unknown,
                    ProviderStatus: null,
                    NewExpiryUtc: null,
                    FailureReason: null,
                    Detail: $"NetworkGuard returned HTTP {(int)response.StatusCode}");
            }

            var payload = await response.Content.ReadFromJsonAsync<OperationResponse>(
                cancellationToken: cancellationToken);

            if (payload is null)
            {
                return new ProviderOperationStatus(
                    Provider,
                    logicalOperationId,
                    ProviderOperationLookupKind.Unknown,
                    ProviderStatus: null,
                    NewExpiryUtc: null,
                    FailureReason: null,
                    Detail: "NetworkGuard returned an empty operation body");
            }

            return Map(logicalOperationId, payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "NetworkGuard operation lookup failed for {LogicalOperationId}",
                logicalOperationId.Value);

            return new ProviderOperationStatus(
                Provider,
                logicalOperationId,
                ProviderOperationLookupKind.Unknown,
                ProviderStatus: null,
                NewExpiryUtc: null,
                FailureReason: null,
                Detail: $"NetworkGuard probe error: {ex.Message}");
        }
    }

    private static ProviderOperationStatus Map(LogicalOperationId logicalOperationId, OperationResponse payload)
    {
        var status = payload.Status?.Trim() ?? string.Empty;

        if (status.Equals("Applied", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderOperationStatus(
                ProviderSystem.NetworkGuard,
                logicalOperationId,
                ProviderOperationLookupKind.ConfirmedApplied,
                payload.Status,
                payload.NewExpiryUtc,
                FailureReason: null,
                Detail: "NetworkGuard AppliedLicenseOperation status=Applied");
        }

        if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderOperationStatus(
                ProviderSystem.NetworkGuard,
                logicalOperationId,
                ProviderOperationLookupKind.ConfirmedFailed,
                payload.Status,
                payload.NewExpiryUtc,
                payload.FailureReason ?? "PROVIDER_FAILED",
                Detail: payload.FailureReason ?? "NetworkGuard AppliedLicenseOperation status=Failed");
        }

        return new ProviderOperationStatus(
            ProviderSystem.NetworkGuard,
            logicalOperationId,
            ProviderOperationLookupKind.Unknown,
            payload.Status,
            payload.NewExpiryUtc,
            payload.FailureReason,
            Detail: $"NetworkGuard returned unrecognized status '{payload.Status}'");
    }

    private sealed record OperationResponse(
        [property: JsonPropertyName("logicalOperationId")] Guid LogicalOperationId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("newExpiryUtc")] DateTimeOffset? NewExpiryUtc,
        [property: JsonPropertyName("failureReason")] string? FailureReason);
}
