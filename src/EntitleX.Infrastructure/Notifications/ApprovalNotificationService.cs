using System.Net;
using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Notifications;

/// <summary>
/// Sends approval notification emails with in-app deep links (never email approve buttons).
/// </summary>
public sealed class ApprovalNotificationService : IApprovalNotificationService
{
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _options;
    private readonly ILogger<ApprovalNotificationService> _logger;

    public ApprovalNotificationService(
        IEmailSender emailSender,
        IOptions<EmailOptions> options,
        ILogger<ApprovalNotificationService> logger)
    {
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    public Task NotifyApprovalStartedAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        CancellationToken cancellationToken = default)
    {
        var link = BuildRequestLink(request.Id.Value);
        var subject = $"EntitleX approval started — {request.ExtensionNumber}";
        var body =
            $"Approval workflow started for extension {request.ExtensionNumber}.{Environment.NewLine}" +
            $"Band: {approvalCase.Band}{Environment.NewLine}" +
            $"Max extension days: {approvalCase.MaxExtensionDays}{Environment.NewLine}" +
            $"Status: {approvalCase.Status}{Environment.NewLine}{Environment.NewLine}" +
            $"Open in EntitleX (decide in the app, not via email):{Environment.NewLine}{link}{Environment.NewLine}";

        return SendSafeAsync(
            ResolveMailbox(EntitleXRoles.RenewalRepresentative),
            "Renewal Representative",
            subject,
            body,
            link,
            cancellationToken);
    }

    public Task NotifyStageReadyAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalStage stage,
        CancellationToken cancellationToken = default)
    {
        var role = ApprovalRules.RoleName(stage.RequiredRole);
        var link = BuildRequestLink(request.Id.Value);
        var subject = $"EntitleX approval required — {request.ExtensionNumber}";
        var body =
            $"Your action is required for extension {request.ExtensionNumber}.{Environment.NewLine}" +
            $"Stage: {stage.Order} ({role}){Environment.NewLine}" +
            $"Max extension days: {approvalCase.MaxExtensionDays}{Environment.NewLine}{Environment.NewLine}" +
            $"Open the request in EntitleX to approve or reject:{Environment.NewLine}{link}{Environment.NewLine}{Environment.NewLine}" +
            "Do not reply to this email to approve — decisions are recorded only in the application." +
            Environment.NewLine;

        return SendSafeAsync(
            ResolveMailbox(role),
            role,
            subject,
            body,
            link,
            cancellationToken);
    }

    public Task NotifyDecisionRecordedAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        var link = BuildRequestLink(request.Id.Value);
        var subject = $"EntitleX decision recorded — {request.ExtensionNumber}";
        var body =
            $"A decision was recorded for extension {request.ExtensionNumber}.{Environment.NewLine}" +
            $"Outcome: {decision.Outcome} by {decision.ActorDisplayName} ({decision.ActorRole}){Environment.NewLine}" +
            $"Approval case status: {approvalCase.Status}{Environment.NewLine}" +
            $"Comment: {decision.Comment ?? "(none)"}{Environment.NewLine}{Environment.NewLine}" +
            $"View in EntitleX:{Environment.NewLine}{link}{Environment.NewLine}";

        return SendSafeAsync(
            ResolveMailbox(EntitleXRoles.RenewalRepresentative),
            "Renewal Representative",
            subject,
            body,
            link,
            cancellationToken);
    }

    private string BuildRequestLink(Guid requestId)
    {
        var baseUrl = _options.AppBaseUrl.TrimEnd('/');
        return $"{baseUrl}/requests/{requestId:D}";
    }

    private string ResolveMailbox(string role) =>
        _options.RoleMailboxes.TryGetValue(role, out var mailbox)
            ? mailbox
            : _options.FromAddress;

    private async Task SendSafeAsync(
        string to,
        string toName,
        string subject,
        string plainBody,
        string link,
        CancellationToken cancellationToken)
    {
        try
        {
            var encoded = WebUtility.HtmlEncode(plainBody).ReplaceLineEndings("<br/>");
            var html =
                $"<p>{encoded}</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">Open in EntitleX</a></p>" +
                "<p><em>Approvals are never completed via email buttons.</em></p>";

            await _emailSender.SendAsync(
                new EmailMessage(to, toName, subject, plainBody, html),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Notifications must not roll back approval decisions.
            _logger.LogWarning(ex, "Failed to send approval notification '{Subject}' to {To}", subject, to);
        }
    }
}
