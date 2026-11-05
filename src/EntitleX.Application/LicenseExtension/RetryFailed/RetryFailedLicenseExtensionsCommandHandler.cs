using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.LicenseExtension.Models;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;
using MediatR;

namespace EntitleX.Application.LicenseExtension.RetryFailed;

public sealed class RetryFailedLicenseExtensionsCommandHandler
    : IRequestHandler<RetryFailedLicenseExtensionsCommand, LicenseExtensionRequestDto>
{
    private readonly ILicenseExtensionRequestRepository _requestRepository;
    private readonly ILicenseExtensionSagaRepository _sagaRepository;
    private readonly IApprovalCaseRepository _approvalRepository;
    private readonly ILicenseExtensionSagaOrchestrator _sagaOrchestrator;
    private readonly IReconciliationGate _reconciliationGate;
    private readonly IRetryClaimStore _retryClaimStore;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IApprovalNotificationService _notifications;

    public RetryFailedLicenseExtensionsCommandHandler(
        ILicenseExtensionRequestRepository requestRepository,
        ILicenseExtensionSagaRepository sagaRepository,
        IApprovalCaseRepository approvalRepository,
        ILicenseExtensionSagaOrchestrator sagaOrchestrator,
        IReconciliationGate reconciliationGate,
        IRetryClaimStore retryClaimStore,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IApprovalNotificationService notifications)
    {
        _requestRepository = requestRepository;
        _sagaRepository = sagaRepository;
        _approvalRepository = approvalRepository;
        _sagaOrchestrator = sagaOrchestrator;
        _reconciliationGate = reconciliationGate;
        _retryClaimStore = retryClaimStore;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task<LicenseExtensionRequestDto> Handle(
        RetryFailedLicenseExtensionsCommand command,
        CancellationToken cancellationToken)
    {
        EnsureRepresentativeAuthorized();

        var existing = await _retryClaimStore.FindByIdempotencyKeyAsync(
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.SourceRequestId.Value != command.SourceRequestId)
            {
                throw new ConflictException(
                    $"Idempotency key '{command.IdempotencyKey}' is already bound to a different source request.");
            }

            var prior = await _requestRepository.GetByIdAsync(existing.RetryRequestId, cancellationToken)
                ?? throw new ConflictException(
                    $"Prior retry '{existing.RetryRequestId.Value}' for idempotency key was not found.");
            return prior.ToDto();
        }

        var sourceId = RequestId.From(command.SourceRequestId);
        var source = await _requestRepository.GetByIdAsync(sourceId, cancellationToken)
            ?? throw new NotFoundException(
                $"License-extension request '{command.SourceRequestId}' was not found.");

        ValidateSource(source);

        var saga = await _sagaRepository.GetByRequestIdAsync(sourceId, cancellationToken)
            ?? throw new ConflictException(
                $"No saga found for request '{command.SourceRequestId}'; cannot evaluate failed items.");

        var blockReason = await _reconciliationGate.GetRetryBlockReasonAsync(
            sourceId,
            saga,
            cancellationToken);
        if (blockReason is not null)
        {
            throw new ConflictException(blockReason);
        }

        var filter = command.LogicalOperationIds?
            .Select(LogicalOperationId.From)
            .ToArray();

        IReadOnlyList<LicenseExtensionRequestItem> failedItems;
        try
        {
            failedItems = FailedItemSelector.SelectEligibleFailedItems(source, saga, filter);
        }
        catch (Domain.Common.DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        var requestedBy = string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? _currentUser.DisplayName
            : _currentUser.UserId;

        var extensionNumber = GenerateExtensionNumber(source);
        var notes = string.IsNullOrWhiteSpace(command.Notes)
            ? $"Selective retry of {source.ExtensionNumber} ({failedItems.Count} failed item(s))."
            : command.Notes.Trim();

        LicenseExtensionRequest retry;
        try
        {
            retry = LicenseExtensionRequest.CreateSelectiveRetry(
                source,
                failedItems,
                extensionNumber,
                requestedBy,
                notes);
        }
        catch (Domain.Common.DomainException ex)
        {
            throw new ConflictException(ex.Message);
        }

        var claimed = await _retryClaimStore.TryClaimItemsAsync(
            sourceId,
            retry.Id,
            command.IdempotencyKey,
            failedItems.Select(i => i.LogicalOperationId).ToList(),
            notes,
            cancellationToken);

        if (!claimed)
        {
            return await ResolveClaimConflictAsync(command, sourceId, cancellationToken);
        }

        EntitleXMeters.RetryAttempts.Add(
            failedItems.Count,
            new KeyValuePair<string, object?>("source", "selective_retry"));

        source.RecordSelectiveRetrySpawned(retry, requestedBy, notes);
        retry.Submit();

        var reuseApproval = ApprovalReuseEvaluator.CanReuseParentApproval(source, failedItems);
        var maxDays = ApprovalRules.ComputeMaxExtensionDays(retry.Items);
        var band = reuseApproval
            ? ApprovalBand.AutoApprove
            : ApprovalRules.EvaluateBand(maxDays);

        var approvalCase = ApprovalCase.Open(retry.Id, maxDays, band);
        ApplyInitialApprovalTransition(retry, band, reuseApproval);

        await _sagaOrchestrator.StartForRequestAsync(retry, band, command.CorrelationId, cancellationToken);

        if (retry.Status == LicenseExtensionRequestStatus.Approved)
        {
            var correlation = Guid.TryParse(command.CorrelationId, out var parsed)
                ? parsed
                : (Guid?)null;
            // Outbox enqueue stays inside the short PG unit of work — no Kafka/SQS/HTTP here.
            await _sagaOrchestrator.DispatchFulfillmentAsync(
                retry,
                correlation,
                causationId: null,
                cancellationToken);
        }

        await _requestRepository.AddAsync(retry, cancellationToken);
        await _approvalRepository.AddAsync(approvalCase, cancellationToken);

        try
        {
            // Single short Postgres transaction: request + approval + saga + outbox + claims.
            // No Kafka / SQS / HTTP inside this boundary.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return await ResolveClaimConflictAsync(command, sourceId, cancellationToken);
        }

        await _notifications.NotifyApprovalStartedAsync(retry, approvalCase, cancellationToken);
        if (approvalCase.Status == ApprovalCaseStatus.Pending && approvalCase.CurrentStage is { } stage)
        {
            await _notifications.NotifyStageReadyAsync(retry, approvalCase, stage, cancellationToken);
        }

        return retry.ToDto();
    }

    private async Task<LicenseExtensionRequestDto> ResolveClaimConflictAsync(
        RetryFailedLicenseExtensionsCommand command,
        RequestId sourceId,
        CancellationToken cancellationToken)
    {
        // Brief retry: winning caller may not have finished persisting the aggregate yet.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var raced = await _retryClaimStore.FindByIdempotencyKeyAsync(
                command.IdempotencyKey,
                cancellationToken);
            if (raced is not null && raced.SourceRequestId.Equals(sourceId))
            {
                var prior = await _requestRepository.GetByIdAsync(
                    raced.RetryRequestId,
                    cancellationToken);
                if (prior is not null)
                {
                    return prior.ToDto();
                }
            }
            else if (raced is null && attempt == 0)
            {
                // Different idempotency key lost on item locks — no prior to return.
                break;
            }

            await Task.Delay(5 * (attempt + 1), cancellationToken);
        }

        throw new ConflictException(
            $"A concurrent selective retry already claims one or more failed items of '{command.SourceRequestId}'.");
    }

    private static bool IsConcurrencyConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            var message = current.Message;
            if (message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || message.Contains("23505", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureRepresentativeAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new ForbiddenException("Authentication is required to retry failed extensions.");
        }

        if (!_currentUser.IsInRole(EntitleXRoles.RenewalRepresentative)
            && !_currentUser.IsInRole(EntitleXRoles.PlatformAdmin))
        {
            throw new ForbiddenException(
                "Only Renewal Representatives (or Platform Admins) may retry failed extensions.");
        }
    }

    private static void ValidateSource(LicenseExtensionRequest source)
    {
        if (source.Status is not (
            LicenseExtensionRequestStatus.PartiallyFulfilled or
            LicenseExtensionRequestStatus.Failed))
        {
            throw new ConflictException(
                $"Request '{source.Id.Value}' status '{source.Status}' is not eligible for selective retry.");
        }
    }

    private static void ApplyInitialApprovalTransition(
        LicenseExtensionRequest aggregate,
        ApprovalBand band,
        bool reusedParentApproval)
    {
        switch (band)
        {
            case ApprovalBand.AutoApprove:
                aggregate.MarkPendingApproval();
                aggregate.RecordApproval(ApprovedScopeHasher.Compute(aggregate));
                if (reusedParentApproval)
                {
                    // Notes already capture lineage; approval version bumped by RecordApproval.
                }

                break;
            case ApprovalBand.Unsupported:
                aggregate.Reject();
                break;
            default:
                aggregate.MarkPendingApproval();
                break;
        }
    }

    private static string GenerateExtensionNumber(LicenseExtensionRequest source)
    {
        // Keep ≤64 chars (DB max). Prefer short unique token + parent id suffix.
        var parentToken = source.Id.Value.ToString("N")[..8];
        return $"EXT-R-{parentToken}-{DateTimeOffset.UtcNow:yyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }
}

