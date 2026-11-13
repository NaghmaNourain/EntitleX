using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.LicenseExtension.Models;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
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
    private readonly ICurrentUser _currentUser;
    private readonly IResourceAuthorizationService _authorization;
    private readonly IReadOnlyDictionary<string, IProviderEntitlementClient> _providers;

    public CreateLicenseExtensionRequestCommandHandler(
        ILicenseExtensionRequestRepository repository,
        IApprovalCaseRepository approvalRepository,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        IUnitOfWork unitOfWork,
        IApprovalNotificationService notifications,
        ICurrentUser currentUser,
        IResourceAuthorizationService authorization,
        IEnumerable<IProviderEntitlementClient> providers)
    {
        _repository = repository;
        _approvalRepository = approvalRepository;
        _sagaOrchestrator = sagaOrchestrator;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _currentUser = currentUser;
        _authorization = authorization;
        _providers = providers.ToDictionary(
            p => p.ProviderName,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<LicenseExtensionRequestDto> Handle(
        CreateLicenseExtensionRequestCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            throw new ForbiddenException("Authentication is required to create a request.");
        }

        if (!_currentUser.IsInRole(EntitleXRoles.RenewalRepresentative)
            && !_currentUser.IsInRole(EntitleXRoles.DealDeskManager)
            && !_currentUser.IsInRole(EntitleXRoles.PlatformAdmin))
        {
            throw new ForbiddenException(
                "Creating license-extension requests requires RenewalRepresentative, DealDeskManager, or PlatformAdmin.");
        }

        // Bind actor from authenticated identity — never from the client body.
        var requestedBy = _currentUser.UserId;

        var extensionNumber = string.IsNullOrWhiteSpace(request.ExtensionNumber)
            ? GenerateExtensionNumber()
            : request.ExtensionNumber.Trim();

        if (await _repository.ExtensionNumberExistsAsync(extensionNumber, cancellationToken))
        {
            throw new ConflictException(
                $"A license-extension request with ExtensionNumber '{extensionNumber}' already exists.");
        }

        var correlationId = Guid.NewGuid().ToString("D");
        var items = new List<(
            EntitlementId EntitlementId,
            ProviderSystem Provider,
            DateTimeOffset CurrentExpiryUtc,
            DateTimeOffset RequestedExpiryUtc)>();

        foreach (var item in request.Items)
        {
            var provider = Enum.Parse<ProviderSystem>(item.Provider, ignoreCase: true);
            if (!_providers.TryGetValue(provider.ToString(), out var client))
            {
                throw new ConflictException(
                    $"No entitlement client is registered for provider '{item.Provider}'.");
            }

            var entitlement = await client.GetByEntitlementIdAsync(
                item.EntitlementId,
                correlationId,
                cancellationToken)
                ?? throw new NotFoundException(
                    $"Entitlement '{item.EntitlementId}' was not found at provider '{provider}'.");

            _authorization.EnsureCanAccessCustomer(entitlement.CustomerId);

            // Always use provider-sourced expiry — client baselines are hints only.
            items.Add((
                EntitlementId.From(item.EntitlementId),
                provider,
                entitlement.ExpiryUtc,
                item.RequestedExpiryUtc));
        }

        var aggregate = LicenseExtensionRequest.CreateFresh(
            extensionNumber,
            requestedBy,
            items);

        aggregate.Submit();

        var maxDays = ApprovalRules.ComputeMaxExtensionDays(aggregate.Items);
        var band = ApprovalRules.EvaluateBand(maxDays);
        var approvalCase = ApprovalCase.Open(aggregate.Id, maxDays, band);

        ApplyInitialApprovalTransition(aggregate, band);

        var saga = await _sagaOrchestrator.StartForRequestAsync(
            aggregate,
            band,
            cancellationToken: cancellationToken);

        if (aggregate.Status == LicenseExtensionRequestStatus.Approved)
        {
            await _sagaOrchestrator.DispatchFulfillmentAsync(
                aggregate,
                saga,
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
