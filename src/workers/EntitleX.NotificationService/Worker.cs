using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace EntitleX.NotificationService;

/// <summary>
/// Phase 4: confirms the configured email provider (Mailpit SMTP or LocalStack SES).
/// Approval emails are dispatched synchronously from the API via
/// <see cref="IApprovalNotificationService"/>; this worker validates the path is reachable.
/// </summary>
public sealed class Worker(
    ILogger<Worker> logger,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = emailOptions.Value;
        logger.LogInformation(
            "NotificationService started. Provider={Provider}, From={From}, AppBaseUrl={AppBaseUrl}",
            options.Provider,
            options.FromAddress,
            options.AppBaseUrl);

        // One-shot connectivity probe (does not approve anything — link-only content).
        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    options.RoleMailboxes.GetValueOrDefault(
                        "PlatformAdmin",
                        options.FromAddress),
                    "Platform Admin",
                    "EntitleX NotificationService ready",
                    $"Notification path is active ({options.Provider}).{Environment.NewLine}" +
                    $"App links use base URL {options.AppBaseUrl}.{Environment.NewLine}" +
                    "Approvals are never completed via email buttons.",
                    null),
                stoppingToken);

            logger.LogInformation("Startup notification probe sent via {Provider}", options.Provider);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Startup notification probe failed (Mailpit/LocalStack may be down). " +
                "API approval emails will log warnings until the provider is available.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("NotificationService heartbeat at {Timestamp:u}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
