using EntitleX.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EntitleX.ExpirationNotification;

/// <summary>
/// Expiration-notification scheduled function. Local host runner via <c>dotnet run</c>;
/// AWS Lambda / EventBridge invoke <see cref="HandlerAsync"/>.
/// </summary>
public sealed class Function
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Function> _logger;

    public Function(IServiceScopeFactory scopeFactory, ILogger<Function> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <param name="providers">
    /// Optional provider filter. EventBridge rules pass CloudShield (daily) or
    /// NetworkGuard (weekly) so each schedule only scans its provider.
    /// </param>
    public async Task<ExpirationNotificationRunResult> HandlerAsync(
        IReadOnlyCollection<string>? providers = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExpirationNotificationService>();
        var result = await service.RunAsync(providers, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "ExpirationNotification complete: scanned={Scanned} sent={Sent} skipped={Skipped} errors={Errors}",
            result.EntitlementsScanned,
            result.NotificationsSent,
            result.SkippedIdempotent,
            result.ProviderErrors);

        return result;
    }
}
