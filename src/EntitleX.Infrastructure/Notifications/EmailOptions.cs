namespace EntitleX.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary><c>Smtp</c> (Mailpit) or <c>Ses</c> (LocalStack / AWS).</summary>
    public string Provider { get; set; } = "Smtp";

    public string FromAddress { get; set; } = "noreply@entitlex.local";

    public string FromDisplayName { get; set; } = "EntitleX";

    /// <summary>Base URL for deep links into the app (not email approve actions).</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:5173";

    public SmtpEmailOptions Smtp { get; set; } = new();

    public SesEmailOptions Ses { get; set; } = new();

    /// <summary>Role → mailbox routing for stage notifications (dev defaults).</summary>
    public Dictionary<string, string> RoleMailboxes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RenewalManager"] = "renewal.manager@entitlex.local",
        ["DealDeskManager"] = "dealdesk.manager@entitlex.local",
        ["RenewalRepresentative"] = "renewal.rep@entitlex.local",
        ["PlatformAdmin"] = "platform.admin@entitlex.local"
    };
}

public sealed class SmtpEmailOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseSsl { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}

public sealed class SesEmailOptions
{
    public string ServiceUrl { get; set; } = "http://localhost:4566";

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";
}
