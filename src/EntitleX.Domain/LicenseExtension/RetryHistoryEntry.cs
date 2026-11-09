using EntitleX.SharedKernel.Ids;

namespace EntitleX.Domain.LicenseExtension;

/// <summary>
/// Append-only record that a selective retry was spawned from a source request.
/// </summary>
public sealed class RetryHistoryEntry
{
    private RetryHistoryEntry()
    {
        // EF Core
        ExtensionNumber = null!;
        CreatedBy = null!;
    }

    private RetryHistoryEntry(
        Guid id,
        RequestId sourceRequestId,
        RequestId retryRequestId,
        string extensionNumber,
        int itemCount,
        string createdBy,
        string? notes,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        SourceRequestId = sourceRequestId;
        RetryRequestId = retryRequestId;
        ExtensionNumber = extensionNumber;
        ItemCount = itemCount;
        CreatedBy = createdBy;
        Notes = notes;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public RequestId SourceRequestId { get; private set; }

    public RequestId RetryRequestId { get; private set; }

    public string ExtensionNumber { get; private set; }

    public int ItemCount { get; private set; }

    public string CreatedBy { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static RetryHistoryEntry Create(
        RequestId sourceRequestId,
        RequestId retryRequestId,
        string extensionNumber,
        int itemCount,
        string createdBy,
        string? notes,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            sourceRequestId,
            retryRequestId,
            extensionNumber,
            itemCount,
            createdBy,
            notes,
            createdAtUtc);
}
