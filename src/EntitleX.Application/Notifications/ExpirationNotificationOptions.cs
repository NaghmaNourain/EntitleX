namespace EntitleX.Application.Notifications;

/// <summary>Configuration for the license-expiration notification scheduled job / Lambda.</summary>
public sealed class ExpirationNotificationOptions
{
    public const string SectionName = "ExpirationNotification";

    /// <summary>CloudShield warning window (days before expiry).</summary>
    public int CloudShieldWarningDays { get; set; } = 30;

    /// <summary>NetworkGuard warning window (days before expiry).</summary>
    public int NetworkGuardWarningDays { get; set; } = 45;

    /// <summary>CloudShield scan cadence in hours (EventBridge / local runner).</summary>
    public int CloudShieldScanIntervalHours { get; set; } = 24;

    /// <summary>NetworkGuard scan cadence in hours (weekly by default).</summary>
    public int NetworkGuardScanIntervalHours { get; set; } = 168;

    /// <summary>
    /// Customers to probe for near-expiry entitlements (providers have no list-all API).
    /// Demo seeds include CUST-1001 / CUST-2002 / CUST-3003.
    /// </summary>
    public List<string> CustomerWatchlist { get; set; } =
    [
        "CUST-1001",
        "CUST-2002",
        "CUST-3003"
    ];

    public int MaxEmailsPerRun { get; set; } = 200;

    public int WarningDaysFor(string provider) =>
        string.Equals(provider, "NetworkGuard", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, NetworkGuardWarningDays)
            : Math.Max(1, CloudShieldWarningDays);

    public TimeSpan ScanCadenceFor(string provider) =>
        string.Equals(provider, "NetworkGuard", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(Math.Max(1, NetworkGuardScanIntervalHours))
            : TimeSpan.FromHours(Math.Max(1, CloudShieldScanIntervalHours));
}
