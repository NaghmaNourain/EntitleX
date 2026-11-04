using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.Models;
using EntitleX.Application.Common.Exceptions;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using MediatR;

namespace EntitleX.Application.Approval.DecideApproval;

public sealed class DecideApprovalCommandHandler
    : IRequestHandler<DecideApprovalCommand, ApprovalCaseDto>
{
    private readonly ILicenseExtensionRequestRepository _requestRepository;
    private readonly IApprovalCaseRepository _approvalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IApprovalNotificationService _notifications;

    public DecideApprovalCommandHandler(
        ILicenseExtensionRequestRepository requestRepository,
        IApprovalCaseRepository approvalRepository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IApprovalNotificationService notifications)
    {
        _requestRepository = requestRepository;
        _approvalRepository = approvalRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task<ApprovalCaseDto> Handle(
        DecideApprovalCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new ForbiddenException("Authentication is required to record an approval decision.");
        }

        var requestId = RequestId.From(command.RequestId);
        var approvalCase = await _approvalRepository.GetByRequestIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(
                $"Approval case for request '{command.RequestId}' was not found.");

        var request = await _requestRepository.GetByIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(
                $"License-extension request '{command.RequestId}' was not found.");

        if (request.Status != LicenseExtensionRequestStatus.PendingApproval)
        {
            throw new ConflictException(
                $"Request '{command.RequestId}' is in status '{request.Status}' and cannot accept decisions.");
        }

        var outcome = Enum.Parse<ApprovalDecisionOutcome>(command.Outcome, ignoreCase: true);

        ApprovalDecision decision;
        try
        {
            decision = approvalCase.RecordDecision(
                outcome,
                _currentUser.UserId,
                _currentUser.Roles,
                _currentUser.DisplayName,
                command.Comment,
                command.CorrelationId);
        }
        catch (UnauthorizedApprovalTransitionException ex)
        {
            throw new ForbiddenException(ex.Message);
        }

        if (approvalCase.Status == ApprovalCaseStatus.Approved)
        {
            request.RecordApproval(ApprovedScopeHasher.Compute(request));
        }
        else if (approvalCase.Status == ApprovalCaseStatus.Rejected)
        {
            request.Reject();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyDecisionRecordedAsync(
            request,
            approvalCase,
            decision,
            cancellationToken);

        if (approvalCase.Status == ApprovalCaseStatus.Pending && approvalCase.CurrentStage is { } next)
        {
            await _notifications.NotifyStageReadyAsync(
                request,
                approvalCase,
                next,
                cancellationToken);
        }

        return approvalCase.ToDto();
    }
}
