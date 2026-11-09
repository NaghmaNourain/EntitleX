using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Domain.Identity;
using MediatR;

namespace EntitleX.Application.Ops.ReplayPoisonOutbox;

public sealed class ReplayPoisonOutboxCommandHandler
    : IRequestHandler<ReplayPoisonOutboxCommand, ReplayPoisonOutboxResult>
{
    private readonly IOutboxPoisonReplayStore _store;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public ReplayPoisonOutboxCommandHandler(
        IOutboxPoisonReplayStore store,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _store = store;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ReplayPoisonOutboxResult> Handle(
        ReplayPoisonOutboxCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated
            || !_currentUser.IsInRole(EntitleXRoles.PlatformAdmin))
        {
            throw new ForbiddenException("Replaying poison outbox rows requires PlatformAdmin.");
        }

        var take = Math.Clamp(command.Take, 1, 200);
        var ids = await _store.ReplayPoisonAsync(command.OutboxMessageId, take, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new ReplayPoisonOutboxResult(ids.Count, ids);
    }
}
