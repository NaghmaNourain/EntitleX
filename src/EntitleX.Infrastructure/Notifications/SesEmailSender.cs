using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using EntitleX.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Notifications;

/// <summary>SES sender targeting LocalStack or real AWS.</summary>
public sealed class SesEmailSender : IEmailSender, IAsyncDisposable
{
    private readonly EmailOptions _options;
    private readonly ILogger<SesEmailSender> _logger;
    private readonly AmazonSimpleEmailServiceClient _client;

    public SesEmailSender(
        IOptions<EmailOptions> options,
        ILogger<SesEmailSender> logger,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _logger = logger;
        _client = CreateClient(_options.Ses, environment);
    }

    public static AmazonSimpleEmailServiceClient CreateClient(
        SesEmailOptions ses,
        IHostEnvironment environment)
    {
        var config = new AmazonSimpleEmailServiceConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(ses.Region)
        };

        var hasCustomEndpoint = !string.IsNullOrWhiteSpace(ses.ServiceUrl);
        if (hasCustomEndpoint)
        {
            config.ServiceURL = ses.ServiceUrl;
            config.AuthenticationRegion = ses.Region;
        }

        var accessKey = ses.AccessKey?.Trim() ?? string.Empty;
        var secretKey = ses.SecretKey?.Trim() ?? string.Empty;
        var usesTestKeys = string.Equals(accessKey, "test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(secretKey, "test", StringComparison.OrdinalIgnoreCase);

        if (usesTestKeys && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Email:Ses AccessKey/SecretKey must not be 'test' outside Development. " +
                "Leave ServiceUrl empty to use the default AWS credential chain (task role / instance profile).");
        }

        if (!hasCustomEndpoint)
        {
            return new AmazonSimpleEmailServiceClient(config);
        }

        return new AmazonSimpleEmailServiceClient(
            new BasicAWSCredentials(accessKey, secretKey),
            config);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var request = new SendEmailRequest
        {
            Source = $"{_options.FromDisplayName} <{_options.FromAddress}>",
            Destination = new Destination
            {
                ToAddresses = [message.ToAddress]
            },
            Message = new Message
            {
                Subject = new Content(message.Subject),
                Body = new Body
                {
                    Text = new Content(message.PlainTextBody),
                    Html = message.HtmlBody is null ? null : new Content(message.HtmlBody)
                }
            }
        };

        await _client.SendEmailAsync(request, cancellationToken);

        _logger.LogInformation(
            "SES email sent to {To} subject {Subject}",
            message.ToAddress,
            message.Subject);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
