using EntitleX.Application.Abstractions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.EntitlementAggregation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.Notifications;

public interface IExpirationNotificationService
{
    /// <param name="providers">
    /// Optional provider filter for schedule-specific runs
    /// (CloudShield daily / NetworkGuard weekly). Null = all registered providers.
    /// </param>
    Task<ExpirationNotificationRunResult> RunAsync(
        IReadOnlyCollection<string>? providers = null,
        DateTimeOffset? nowUtc = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

public sealed record ExpirationNotificationRunResult(
    int EntitlementsScanned,
    int NotificationsSent,
    int SkippedIdempotent,
    int ProviderErrors);

public sealed class ExpirationNotificationService : IExpirationNotificationService
{
    private readonly IEnumerable<IProviderEntitlementClient> _providers;
    private readonly IExpirationNotificationStore _store;
    private readonly IApprovalNotificationService _notifications;
    private readonly IAuditEventStore _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ExpirationNotificationOptions _options;
    private readonly EmailPortalOptions _portal;
    private readonly ILogger<ExpirationNotificationService> _logger;

    public ExpirationNotificationService(
        IEnumerable<IProviderEntitlementClient> providers,
        IExpirationNotificationStore store,
        IApprovalNotificationService notifications,
        IAuditEventStore audit,
        IUnitOfWork unitOfWork,
        IOptions<ExpirationNotificationOptions> options,
        IOptions<EmailPortalOptions> portal,
        ILogger<ExpirationNotificationService> logger)
    {
        _providers = providers;
        _store = store;
        _notifications = notifications;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _portal = portal.Value;
        _logger = logger;
    }

    public async Task<ExpirationNotificationRunResult> RunAsync(
        IReadOnlyCollection<string>? providers = null,
        DateTimeOffset? nowUtc = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var correlation = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();

        var filter = providers?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var clients = _providers
            .Where(c => filter is null || filter.Count == 0 || filter.Contains(c.ProviderName))
            .ToList();

        var scanned = 0;
        var sent = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var client in clients)
        {
            var warningDays = _options.WarningDaysFor(client.ProviderName);
            var cadence = _options.ScanCadenceFor(client.ProviderName);
            var horizon = now.AddDays(warningDays);

            foreach (var customerId in _options.CustomerWatchlist.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IReadOnlyList<NormalizedEntitlementDto> entitlements;
                try
                {
                    entitlements = await client.DiscoverByCustomerAsync(
                        customerId,
                        correlation,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(
                        ex,
                        "Expiration discovery failed for {Provider} customer {CustomerId}",
                        client.ProviderName,
                        customerId);
                    continue;
                }

                foreach (var entitlement in entitlements)
                {
                    scanned++;

                    if (entitlement.ExpiryUtc < now || entitlement.ExpiryUtc > horizon)
                    {
                        continue;
                    }

                    if (sent >= _options.MaxEmailsPerRun)
                    {
                        break;
                    }

                    var windowKey = NotificationWindowKeys.ForExpiration(
                        client.ProviderName,
                        entitlement.EntitlementId,
                        now,
                        cadence);

                    if (await _store.WasNotifiedAsync(windowKey, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    var portalLink = BuildPortalLink(entitlement);
                    var record = new ExpirationNotificationRecordDto(
                        Guid.NewGuid(),
                        client.ProviderName,
                        entitlement.EntitlementId,
                        entitlement.CustomerId,
                        windowKey,
                        entitlement.ExpiryUtc,
                        now);

                    if (!await _store.TryRecordAsync(record, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    await _notifications.NotifyExpirationWarningAsync(
                        entitlement,
                        portalLink,
                        cancellationToken);

                    await _audit.AppendAsync(
                        new AuditEventRecord(
                            Guid.NewGuid(),
                            AuditEventTypes.ExpirationNotificationSent,
                            RequestId: null,
                            LogicalOperationId: null,
                            Actor: "expiration-notification",
                            Detail:
                            $"{client.ProviderName} entitlement {entitlement.EntitlementId} " +
                            $"customer={entitlement.CustomerId} product={entitlement.ProductCode} " +
                            $"expires={entitlement.ExpiryUtc:u} window={windowKey}",
                            correlation,
                            now),
                        cancellationToken);

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    sent++;
                }
            }
        }

        _logger.LogInformation(
            "Expiration notification run scanned={Scanned} sent={Sent} skipped={Skipped} errors={Errors} correlation={Correlation}",
            scanned,
            sent,
            skipped,
            errors,
            correlation);

        return new ExpirationNotificationRunResult(scanned, sent, skipped, errors);
    }

    private string BuildPortalLink(NormalizedEntitlementDto entitlement)
    {
        var baseUrl = (_portal.AppBaseUrl ?? "http://localhost:5173").TrimEnd('/');
        return $"{baseUrl}/entitlements/{Uri.EscapeDataString(entitlement.Provider)}/{Uri.EscapeDataString(entitlement.EntitlementId)}";
    }
}

/// <summary>
/// Thin portal options so Application does not depend on Infrastructure EmailOptions.
/// Bound from the same <c>Email</c> config section in hosts.
/// </summary>
public sealed class EmailPortalOptions
{
    public const string SectionName = "Email";

    public string AppBaseUrl { get; set; } = "http://localhost:5173";
}
