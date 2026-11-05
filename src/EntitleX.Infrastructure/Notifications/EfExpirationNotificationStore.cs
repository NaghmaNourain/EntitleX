using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Notifications;

public sealed class EfExpirationNotificationStore : IExpirationNotificationStore
{
    private readonly EntitleXDbContext _db;

    public EfExpirationNotificationStore(EntitleXDbContext db)
    {
        _db = db;
    }

    public Task<bool> WasNotifiedAsync(
        string windowKey,
        CancellationToken cancellationToken = default) =>
        _db.ExpirationNotificationRecords
            .AsNoTracking()
            .AnyAsync(x => x.WindowKey == windowKey, cancellationToken);

    public async Task<bool> TryRecordAsync(
        ExpirationNotificationRecordDto record,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.ExpirationNotificationRecords
            .AnyAsync(x => x.WindowKey == record.WindowKey, cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.ExpirationNotificationRecords.Add(new ExpirationNotificationRecord(
            record.Id,
            record.Provider,
            record.EntitlementId,
            record.CustomerId,
            record.WindowKey,
            record.ExpiryUtc,
            record.NotifiedAtUtc));

        return true;
    }
}
