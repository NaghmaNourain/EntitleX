using System.Net;
using System.Net.Mail;
using EntitleX.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Notifications;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15_000
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
        {
            client.Credentials = new NetworkCredential(
                _options.Smtp.Username,
                _options.Smtp.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromDisplayName),
            Subject = message.Subject,
            Body = message.PlainTextBody,
            IsBodyHtml = false
        };

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(message.PlainTextBody, null, "text/plain"));
            mail.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));
        }

        mail.To.Add(new MailAddress(message.ToAddress, message.ToDisplayName));

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(mail, cancellationToken);

        _logger.LogInformation(
            "SMTP email sent to {To} subject {Subject}",
            message.ToAddress,
            message.Subject);
    }
}
