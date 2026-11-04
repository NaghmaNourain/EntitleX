namespace EntitleX.Messaging.Abstractions;

/// <summary>Metrics hooks for outbox publishing (Phase 5 stubs; wire OTel later).</summary>
public interface IOutboxMetrics
{
    void RecordClaimed(int count);

    void RecordPublished(string messageType, string destinationKind);

    void RecordRetry(string messageType, int attempt);

    void RecordPoison(string messageType);

    void RecordPublishFailure(string messageType);
}
