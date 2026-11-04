namespace EntitleX.Domain.Saga;

/// <summary>Immutable history entry for a saga state transition.</summary>
public sealed class SagaTransition
{
    private SagaTransition()
    {
        // EF Core
        Reason = null!;
    }

    private SagaTransition(
        Guid id,
        SagaState fromState,
        SagaState toState,
        string reason,
        string? actor,
        string? correlationId,
        DateTimeOffset occurredAtUtc,
        string? metadata)
    {
        Id = id;
        FromState = fromState;
        ToState = toState;
        Reason = reason;
        Actor = actor;
        CorrelationId = correlationId;
        OccurredAtUtc = occurredAtUtc;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }

    public SagaState FromState { get; private set; }

    public SagaState ToState { get; private set; }

    public string Reason { get; private set; }

    public string? Actor { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string? Metadata { get; private set; }

    internal static SagaTransition Create(
        SagaState fromState,
        SagaState toState,
        string reason,
        string? actor,
        string? correlationId,
        DateTimeOffset occurredAtUtc,
        string? metadata = null) =>
        new(
            Guid.NewGuid(),
            fromState,
            toState,
            reason,
            actor,
            correlationId,
            occurredAtUtc,
            metadata);
}
