namespace EntitleX.Application.Abstractions;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage(
    string ToAddress,
    string ToDisplayName,
    string Subject,
    string PlainTextBody,
    string? HtmlBody = null);
