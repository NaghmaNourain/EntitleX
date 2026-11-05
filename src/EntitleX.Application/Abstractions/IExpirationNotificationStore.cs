namespace EntitleX.Application.Abstractions;

public sealed record ExpirationNotificationRecordDto(
    Guid Id,
    string Provider,
    string EntitlementId,
    string CustomerId,
    string WindowKey,
    DateTimeOffset ExpiryUtc,
    DateTimeOffset NotifiedAtUtc);

public interface IExpirationNotificationStore
{
    Task<bool> WasNotifiedAsync(
        string windowKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a notification. Returns false when the window key already exists.
    /// </summary>
    Task<bool> TryRecordAsync(
        ExpirationNotificationRecordDto record,
        CancellationToken cancellationToken = default);
}
