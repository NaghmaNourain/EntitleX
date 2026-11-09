namespace EntitleX.Infrastructure.Outbox;

public enum OutboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Published = 2,
    Poison = 3
}

public enum OutboxDestinationKind
{
    Sqs = 0,
    Kafka = 1
}

/// <summary>
/// Transactional outbox row. Persisted in the same Postgres transaction as the
/// business change that produced the integration message.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        MessageType = null!;
        PayloadJson = null!;
        Destination = null!;
        SchemaVersion = null!;
    }

    private OutboxMessage(
        Guid id,
        Guid messageId,
        string messageType,
        string payloadJson,
        OutboxDestinationKind destinationKind,
        string destination,
        string? partitionKey,
        Guid? correlationId,
        Guid? causationId,
        Guid? requestId,
        Guid? logicalOperationId,
        string schemaVersion,
        int maxAttempts,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        MessageId = messageId;
        MessageType = messageType;
        PayloadJson = payloadJson;
        DestinationKind = destinationKind;
        Destination = destination;
        PartitionKey = partitionKey;
        CorrelationId = correlationId;
        CausationId = causationId;
        RequestId = requestId;
        LogicalOperationId = logicalOperationId;
        SchemaVersion = schemaVersion;
        Status = OutboxMessageStatus.Pending;
        AttemptCount = 0;
        MaxAttempts = maxAttempts;
        CreatedAtUtc = createdAtUtc;
        AvailableAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid MessageId { get; private set; }

    public string MessageType { get; private set; }

    public string PayloadJson { get; private set; }

    public OutboxDestinationKind DestinationKind { get; private set; }

    public string Destination { get; private set; }

    public string? PartitionKey { get; private set; }

    public OutboxMessageStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset AvailableAtUtc { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public string? LockedBy { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public Guid? CorrelationId { get; private set; }

    public Guid? CausationId { get; private set; }

    public Guid? RequestId { get; private set; }

    public Guid? LogicalOperationId { get; private set; }

    public string SchemaVersion { get; private set; }

    public static OutboxMessage Create(
        Guid messageId,
        string messageType,
        string payloadJson,
        OutboxDestinationKind destinationKind,
        string destination,
        string? partitionKey = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        Guid? requestId = null,
        Guid? logicalOperationId = null,
        string schemaVersion = "1",
        int maxAttempts = 8,
        DateTimeOffset? createdAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        return new OutboxMessage(
            Guid.NewGuid(),
            messageId,
            messageType.Trim(),
            payloadJson,
            destinationKind,
            destination.Trim(),
            string.IsNullOrWhiteSpace(partitionKey) ? null : partitionKey.Trim(),
            correlationId,
            causationId,
            requestId,
            logicalOperationId,
            string.IsNullOrWhiteSpace(schemaVersion) ? "1" : schemaVersion.Trim(),
            maxAttempts <= 0 ? 8 : maxAttempts,
            createdAtUtc ?? DateTimeOffset.UtcNow);
    }

    public void MarkPublished(DateTimeOffset publishedAtUtc)
    {
        Status = OutboxMessageStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        LockedUntilUtc = null;
        LockedBy = null;
        LastError = null;
    }

    public void MarkRetry(string error, DateTimeOffset availableAtUtc)
    {
        Status = OutboxMessageStatus.Pending;
        LastError = Truncate(error);
        AvailableAtUtc = availableAtUtc;
        LockedUntilUtc = null;
        LockedBy = null;
    }

    public void MarkPoison(string error, DateTimeOffset atUtc)
    {
        Status = OutboxMessageStatus.Poison;
        LastError = Truncate(error);
        LockedUntilUtc = null;
        LockedBy = null;
        PublishedAtUtc = null;
        AvailableAtUtc = atUtc;
    }

    /// <summary>Operator replay: Poison → Pending with a fresh attempt budget.</summary>
    public void ReplayFromPoison(DateTimeOffset atUtc, int? resetMaxAttempts = null)
    {
        if (Status != OutboxMessageStatus.Poison)
        {
            throw new InvalidOperationException(
                $"Only Poison outbox rows can be replayed (was '{Status}').");
        }

        Status = OutboxMessageStatus.Pending;
        AttemptCount = 0;
        if (resetMaxAttempts is > 0)
        {
            MaxAttempts = resetMaxAttempts.Value;
        }

        LastError = null;
        LockedUntilUtc = null;
        LockedBy = null;
        PublishedAtUtc = null;
        AvailableAtUtc = atUtc;
    }

    private static string Truncate(string error) =>
        error.Length <= 2000 ? error : error[..2000];
}
