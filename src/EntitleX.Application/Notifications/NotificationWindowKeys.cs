using System.Globalization;

namespace EntitleX.Application.Notifications;

/// <summary>Builds stable idempotency window keys for scheduled notification dedupe.</summary>
public static class NotificationWindowKeys
{
    /// <summary>
    /// Reminder window: one reminder per approval case + stage.
    /// </summary>
    public static string ForApprovalReminder(Guid approvalCaseId, int stageOrder) =>
        $"approval-reminder:{approvalCaseId:D}:stage-{stageOrder}";

    /// <summary>
    /// Expiration notification window keyed by provider scan cadence
    /// (daily → calendar day; weekly+ → ISO week).
    /// </summary>
    public static string ForExpiration(
        string provider,
        string entitlementId,
        DateTimeOffset nowUtc,
        TimeSpan scanCadence)
    {
        var bucket = scanCadence <= TimeSpan.FromHours(24)
            ? nowUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : IsoWeekBucket(nowUtc);

        return $"expiry:{provider}:{entitlementId}:{bucket}";
    }

    private static string IsoWeekBucket(DateTimeOffset nowUtc)
    {
        var date = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var week = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var year = ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return string.Create(CultureInfo.InvariantCulture, $"{year:D4}-W{week:D2}");
    }
}
