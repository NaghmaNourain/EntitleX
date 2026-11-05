using System.Text.Json;

namespace EntitleX.Messaging.Contracts;

/// <summary>
/// Integration-message envelope with tracing identifiers and schema versioning stubs.
/// </summary>
public sealed record MessageEnvelope(
    string SchemaVersion,
    string MessageType,
    Guid MessageId,
    Guid? CorrelationId,
    Guid? CausationId,
    Guid? RequestId,
    Guid? LogicalOperationId,
    DateTimeOffset OccurredAtUtc,
    JsonElement Payload)
{
    public const string CurrentSchemaVersion = "1";

    public static MessageEnvelope Create<TPayload>(
        string messageType,
        TPayload payload,
        Guid messageId,
        Guid? correlationId = null,
        Guid? causationId = null,
        Guid? requestId = null,
        Guid? logicalOperationId = null,
        string schemaVersion = CurrentSchemaVersion,
        DateTimeOffset? occurredAtUtc = null)
    {
        var element = JsonSerializer.SerializeToElement(payload, MessagingJson.Options);
        return new MessageEnvelope(
            schemaVersion,
            messageType,
            messageId,
            correlationId,
            causationId,
            requestId,
            logicalOperationId,
            occurredAtUtc ?? DateTimeOffset.UtcNow,
            element);
    }

    public T DeserializePayload<T>() =>
        Payload.Deserialize<T>(MessagingJson.Options)
        ?? throw new InvalidOperationException(
            $"Failed to deserialize payload for message type '{MessageType}'.");
}
