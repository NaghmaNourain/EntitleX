using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Identity;
using MediatR;

namespace EntitleX.Application.Reconciliation.ManualReconcile;

public sealed class ManualReconcileCommandHandler
    : IRequestHandler<ManualReconcileCommand, ReconciliationRunResult>
{
    private readonly IReconciliationService _reconciliation;
    private readonly ILicenseExtensionSagaRepository _sagaRepository;
    private readonly IAuditEventStore _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public ManualReconcileCommandHandler(
        IReconciliationService reconciliation,
        ILicenseExtensionSagaRepository sagaRepository,
        IAuditEventStore audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser)
    {
        _reconciliation = reconciliation;
        _sagaRepository = sagaRepository;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<ReconciliationRunResult> Handle(
        ManualReconcileCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(EntitleXRoles.PlatformAdmin))
        {
            throw new ForbiddenException(
                "Manual reconciliation requires the PlatformAdmin role.");
        }

        var requestId = RequestId.From(command.RequestId);
        _ = await _sagaRepository.GetByRequestIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(
                $"No saga found for request '{command.RequestId}'.");

        var actor = string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? "platform-admin"
            : _currentUser.UserId;

        await _audit.AppendAsync(
            new AuditEventRecord(
                Guid.NewGuid(),
                AuditEventTypes.ReconciliationManualTriggered,
                requestId,
                LogicalOperationId: null,
                actor,
                "PlatformAdmin requested manual reconciliation",
                command.CorrelationId,
                DateTimeOffset.UtcNow),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await _reconciliation.ReconcileRequestAsync(
            requestId,
            actor,
            command.CorrelationId,
            cancellationToken);
    }
}
