using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.Models;
using EntitleX.Application.Common.Exceptions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using MediatR;

namespace EntitleX.Application.Approval.StartApproval;

public sealed class StartApprovalCommandHandler
    : IRequestHandler<StartApprovalCommand, ApprovalCaseDto>
{
    private readonly ILicenseExtensionRequestRepository _requestRepository;
    private readonly IApprovalCaseRepository _approvalRepository;
    private readonly ILicenseExtensionSagaOrchestrator _sagaOrchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IApprovalNotificationService _notifications;
    private readonly ICurrentUser _currentUser;

    public StartApprovalCommandHandler(
        ILicenseExtensionRequestRepository requestRepository,
        IApprovalCaseRepository approvalRepository,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        IUnitOfWork unitOfWork,
        IApprovalNotificationService notifications,
        ICurrentUser currentUser)
    {
        _requestRepository = requestRepository;
        _approvalRepository = approvalRepository;
        _sagaOrchestrator = sagaOrchestrator;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<ApprovalCaseDto> Handle(
        StartApprovalCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new ForbiddenException("Authentication is required to start approval.");
        }

        if (!_currentUser.IsInRole(EntitleXRoles.RenewalRepresentative)
            && !_currentUser.IsInRole(EntitleXRoles.PlatformAdmin))
        {
            throw new ForbiddenException(
                "Starting approval requires RenewalRepresentative or PlatformAdmin.");
        }

        var requestId = RequestId.From(command.RequestId);
        var existing = await _approvalRepository.GetByRequestIdAsync(requestId, cancellationToken);
        if (existing is not null)
        {
            throw new ConflictException(
                $"An approval case already exists for request '{command.RequestId}'.");
        }

        var request = await _requestRepository.GetByIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(
                $"License-extension request '{command.RequestId}' was not found.");

        if (request.Status is not (
            LicenseExtensionRequestStatus.Submitted or
            LicenseExtensionRequestStatus.PendingApproval))
        {
            throw new ConflictException(
                $"Request '{command.RequestId}' is in status '{request.Status}' and cannot start approval.");
        }

        var maxDays = ApprovalRules.ComputeMaxExtensionDays(request.Items);
        var band = ApprovalRules.EvaluateBand(maxDays);
        var approvalCase = ApprovalCase.Open(requestId, maxDays, band);

        if (request.Status == LicenseExtensionRequestStatus.Submitted)
        {
            if (band == ApprovalBand.Unsupported)
            {
                request.Reject();
            }
            else if (band == ApprovalBand.AutoApprove)
            {
                request.MarkPendingApproval();
                request.RecordApproval(ApprovedScopeHasher.Compute(request));
            }
            else
            {
                request.MarkPendingApproval();
            }
        }
        else if (band == ApprovalBand.AutoApprove)
        {
            request.RecordApproval(ApprovedScopeHasher.Compute(request));
        }
        else if (band == ApprovalBand.Unsupported)
        {
            request.Reject();
        }

        // Start before dispatch; pass the returned saga so dispatch does not re-query
        // an Added-but-unsaved aggregate (repository Local is a safety net).
        var saga = await _sagaOrchestrator.StartForRequestAsync(
            request,
            band,
            command.CorrelationId,
            cancellationToken);

        if (band == ApprovalBand.AutoApprove &&
            request.Status == LicenseExtensionRequestStatus.Approved)
        {
            await _sagaOrchestrator.DispatchFulfillmentAsync(
                request,
                saga,
                correlationId: null,
                causationId: null,
                cancellationToken);
        }

        await _approvalRepository.AddAsync(approvalCase, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyApprovalStartedAsync(request, approvalCase, cancellationToken);

        if (approvalCase.Status == ApprovalCaseStatus.Pending && approvalCase.CurrentStage is { } stage)
        {
            await _notifications.NotifyStageReadyAsync(
                request,
                approvalCase,
                stage,
                cancellationToken);
        }

        return approvalCase.ToDto();
    }
}
