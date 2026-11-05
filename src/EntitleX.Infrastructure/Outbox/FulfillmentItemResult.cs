namespace EntitleX.Infrastructure.Outbox;

/// <summary>
/// Minimal fulfillment outcome ledger (pre-saga). Unique on LogicalOperationId
/// so completion/failure events are idempotent.
/// </summary>
public sealed class FulfillmentItemResult
{
    private FulfillmentItemResult()
    {
        EntitlementId = null!;
        Provider = null!;
    }

    public FulfillmentItemResult(
        Guid requestId,
        Guid requestItemId,
        Guid logicalOperationId,
        string entitlementId,
        string provider,
        bool succeeded,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset recordedAtUtc)
    {
        Id = Guid.NewGuid();
        RequestId = requestId;
        RequestItemId = requestItemId;
        LogicalOperationId = logicalOperationId;
        EntitlementId = entitlementId;
        Provider = provider;
        Succeeded = succeeded;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid RequestItemId { get; private set; }

    public Guid LogicalOperationId { get; private set; }

    public string EntitlementId { get; private set; }

    public string Provider { get; private set; }

    public bool Succeeded { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }
}
