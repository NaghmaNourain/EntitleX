using MediatR;

namespace EntitleX.Application.Ops.ReplayPoisonOutbox;

public sealed record ReplayPoisonOutboxCommand(
    Guid? OutboxMessageId = null,
    int Take = 25) : IRequest<ReplayPoisonOutboxResult>;

public sealed record ReplayPoisonOutboxResult(
    int ReplayedCount,
    IReadOnlyList<Guid> ReplayedIds);
