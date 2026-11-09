namespace EntitleX.Application.Abstractions;

public interface IOutboxPoisonReplayStore
{
    Task<IReadOnlyList<Guid>> ReplayPoisonAsync(
        Guid? outboxMessageId,
        int take,
        CancellationToken cancellationToken = default);
}
