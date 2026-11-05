using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.LicenseExtension.Models;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using MediatR;

namespace EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;

public sealed class CreateLicenseExtensionRequestCommandHandler
    : IRequestHandler<CreateLicenseExtensionRequestCommand, LicenseExtensionRequestDto>
{
    private readonly ILicenseExtensionRequestRepository _repository;
    private readonly IApprovalCaseRepository _approvalRepository;
    private readonly ILicenseExtensionSagaOrchestrator _sagaOrchestrator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IApprovalNotificationService _notifications;

    public CreateLicenseExtensionRequestCommandHandler(
        ILicenseExtensionRequestRepository repository,
        IApprovalCaseRepository approvalRepository,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        IUnitOfWork unitOfWork,
        IApprovalNotificationService notifications)
    {
        _repository = repository;
        _approvalRepository = approvalRepository;
        _sagaOrchestrator = sagaOrchestrator;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
    }

    public async Task<LicenseExtensionRequestDto> Handle(
        CreateLicenseExtensionRequestCommand request,
        CancellationToken cancellationToken)
    {
        var extensionNumber = string.IsNullOrWhiteSpace(request.ExtensionNumber)
            ? GenerateExtensionNumber()
            : request.ExtensionNumber.Trim();

        if (await _repository.ExtensionNumberExistsAsync(extensionNumber, cancellationToken))
        {
            throw new ConflictException(
                $"A license-extension request with ExtensionNumber '{extensionNumber}' already exists.");
        }

        var items = request.Items.Select(i => (
            EntitlementId: EntitlementId.From(i.EntitlementId),
            Provider: Enum.Parse<ProviderSystem>(i.Provider, ignoreCase: true),
            CurrentExpiryUtc: i.CurrentExpiryUtc,
            RequestedExpiryUtc: i.RequestedExpiryUtc));

        var aggregate = LicenseExtensionRequest.CreateFresh(
            extensionNumber,
            request.RequestedBy,
            items);

        aggregate.Submit();

        var maxDays = ApprovalRules.ComputeMaxExtensionDays(aggregate.Items);
        var band = ApprovalRules.EvaluateBand(maxDays);
        var approvalCase = ApprovalCase.Open(aggregate.Id, maxDays, band);

        ApplyInitialApprovalTransition(aggregate, band);

        await _sagaOrchestrator.StartForRequestAsync(aggregate, band, cancellationToken: cancellationToken);

        if (aggregate.Status == LicenseExtensionRequestStatus.Approved)
        {
            await _sagaOrchestrator.DispatchFulfillmentAsync(
                aggregate,
                correlationId: null,
                causationId: null,
                cancellationToken);
        }

        await _repository.AddAsync(aggregate, cancellationToken);
        await _approvalRepository.AddAsync(approvalCase, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifications.NotifyApprovalStartedAsync(aggregate, approvalCase, cancellationToken);

        if (approvalCase.Status == ApprovalCaseStatus.Pending && approvalCase.CurrentStage is { } stage)
        {
            await _notifications.NotifyStageReadyAsync(
                aggregate,
                approvalCase,
                stage,
                cancellationToken);
        }

        return aggregate.ToDto();
    }

    private static void ApplyInitialApprovalTransition(
        LicenseExtensionRequest aggregate,
        ApprovalBand band)
    {
        switch (band)
        {
            case ApprovalBand.AutoApprove:
                aggregate.MarkPendingApproval();
                aggregate.RecordApproval(ApprovedScopeHasher.Compute(aggregate));
                break;
            case ApprovalBand.Unsupported:
                aggregate.Reject();
                break;
            default:
                aggregate.MarkPendingApproval();
                break;
        }
    }

    private static string GenerateExtensionNumber() =>
        $"EXT-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
}
