using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.EntitlementAggregation;

/// <summary>
/// Fan-out to all provider clients; returns partial results when some providers fail.
/// Does not open or hold any platform DB transaction across remote HTTP.
/// </summary>
public sealed class EntitlementAggregationService : IEntitlementAggregationService
{
    private readonly IReadOnlyList<IProviderEntitlementClient> _providers;
    private readonly ILogger<EntitlementAggregationService> _logger;

    public EntitlementAggregationService(
        IEnumerable<IProviderEntitlementClient> providers,
        ILogger<EntitlementAggregationService> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public async Task<CustomerEntitlementAggregationDto> AggregateForCustomerAsync(
        string customerId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Aggregating entitlements for {CustomerId} across {ProviderCount} providers. CorrelationId={CorrelationId}",
            customerId,
            _providers.Count,
            correlationId);

        var tasks = _providers
            .Select(p => QueryProviderAsync(p, customerId, correlationId, cancellationToken))
            .ToArray();

        var contributions = await Task.WhenAll(tasks);

        var entitlements = contributions
            .Where(c => c.Succeeded)
            .SelectMany(c => c.Entitlements)
            .OrderBy(e => e.Provider)
            .ThenBy(e => e.EntitlementId)
            .ToList();

        var providerDtos = contributions
            .Select(c => new ProviderContributionDto(
                c.Provider,
                c.Succeeded,
                c.Entitlements.Count,
                c.Error,
                c.Duration))
            .ToList();

        var isPartial = providerDtos.Any(p => !p.Succeeded);

        return new CustomerEntitlementAggregationDto(
            customerId,
            correlationId,
            entitlements,
            providerDtos,
            isPartial,
            Page: 1,
            PageSize: entitlements.Count == 0 ? 100 : entitlements.Count,
            TotalCount: entitlements.Count);
    }

    private async Task<ProviderResult> QueryProviderAsync(
        IProviderEntitlementClient client,
        string customerId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var items = await client.DiscoverByCustomerAsync(customerId, correlationId, cancellationToken);
            var duration = DateTimeOffset.UtcNow - started;

            _logger.LogInformation(
                "Provider {Provider} returned {Count} entitlements for {CustomerId} in {DurationMs}ms. CorrelationId={CorrelationId}",
                client.ProviderName,
                items.Count,
                duration.TotalMilliseconds,
                customerId,
                correlationId);

            return new ProviderResult(client.ProviderName, true, items, null, duration);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            var duration = DateTimeOffset.UtcNow - started;
            _logger.LogWarning(
                ex,
                "Provider {Provider} failed for {CustomerId} after {DurationMs}ms. CorrelationId={CorrelationId}",
                client.ProviderName,
                customerId,
                duration.TotalMilliseconds,
                correlationId);

            return new ProviderResult(
                client.ProviderName,
                false,
                Array.Empty<NormalizedEntitlementDto>(),
                ex.Message,
                duration);
        }
    }

    /// <summary>
    /// Provider HTTP/resilience failures (including Polly optimistic timeouts that surface as
    /// <see cref="OperationCanceledException"/>) must not fail the whole aggregation.
    /// Only honor true client/request abort when <paramref name="cancellationToken"/> is canceled.
    /// </summary>
    private static bool IsProviderFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private sealed record ProviderResult(
        string Provider,
        bool Succeeded,
        IReadOnlyList<NormalizedEntitlementDto> Entitlements,
        string? Error,
        TimeSpan Duration);
}
