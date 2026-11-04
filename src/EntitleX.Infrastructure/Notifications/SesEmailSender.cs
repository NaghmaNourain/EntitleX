using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using EntitleX.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Notifications;

/// <summary>SES sender targeting LocalStack or real AWS.</summary>
public sealed class SesEmailSender : IEmailSender, IAsyncDisposable
{
    private readonly EmailOptions _options;
    private readonly ILogger<SesEmailSender> _logger;
    private readonly AmazonSimpleEmailServiceClient _client;

    public SesEmailSender(IOptions<EmailOptions> options, ILogger<SesEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;

        var credentials = new BasicAWSCredentials(
            _options.Ses.AccessKey,
            _options.Ses.SecretKey);

        var config = new AmazonSimpleEmailServiceConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Ses.Region),
            ServiceURL = _options.Ses.ServiceUrl,
            AuthenticationRegion = _options.Ses.Region
        };

        // LocalStack uses path-style / custom endpoint.
        if (!string.IsNullOrWhiteSpace(_options.Ses.ServiceUrl))
        {
            config.ServiceURL = _options.Ses.ServiceUrl;
        }

        _client = new AmazonSimpleEmailServiceClient(credentials, config);
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
