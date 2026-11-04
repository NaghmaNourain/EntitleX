namespace EntitleX.Infrastructure.Notifications;

/// <summary>Idempotent record of an expiration warning email for a notification window.</summary>
public sealed class ExpirationNotificationRecord
{
    private ExpirationNotificationRecord()
    {
        Provider = null!;
        EntitlementId = null!;
        CustomerId = null!;
        WindowKey = null!;
    }

    public ExpirationNotificationRecord(
        Guid id,
        string provider,
        string entitlementId,
        string customerId,
        string windowKey,
        DateTimeOffset expiryUtc,
        DateTimeOffset notifiedAtUtc)
    {
        Id = id;
        Provider = provider;
        EntitlementId = entitlementId;
        CustomerId = customerId;
        WindowKey = windowKey;
        ExpiryUtc = expiryUtc;
        NotifiedAtUtc = notifiedAtUtc;
    }

    public Guid Id { get; private set; }

    public string Provider { get; private set; }

    public string EntitlementId { get; private set; }

    public string CustomerId { get; private set; }

    public string WindowKey { get; private set; }

    public DateTimeOffset ExpiryUtc { get; private set; }

    public DateTimeOffset NotifiedAtUtc { get; private set; }
}
