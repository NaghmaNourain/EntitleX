using EntitleX.Application.Abstractions;
using EntitleX.Application.Notifications;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.Reconciliation;

/// <summary>
/// Applies provider-confirmed outcomes onto the saga / local ledger / audit trail.
/// </summary>
public sealed class ReconciliationApplier
{
    private readonly ILocalFulfillmentOutcomeStore _localOutcomes;
    private readonly IAuditEventStore _audit;
    private readonly ReconciliationAgingOptions _aging;
    private readonly ILogger<ReconciliationApplier> _logger;

    public ReconciliationApplier(
        ILocalFulfillmentOutcomeStore localOutcomes,
        IAuditEventStore audit,
        IOptions<ReconciliationAgingOptions> aging,
        ILogger<ReconciliationApplier> logger)
    {
        _localOutcomes = localOutcomes;
        _audit = audit;
        _aging = aging.Value;
        _logger = logger;
    }

    public bool ShouldAgeUncertainToFailed(SagaItemOutcome outcome)
    {
        if (_aging.UncertainMaxAgeDays <= 0)
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - outcome.UpdatedAtUtc;
        return age >= TimeSpan.FromDays(_aging.UncertainMaxAgeDays);
    }

    public async Task<ReconciliationItemResult> ApplyConfirmedSuccessAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        LicenseExtensionRequestItem requestItem,
        SagaItemOutcome outcome,
        ProviderOperationStatus status,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var providerName = requestItem.Provider.ToString();

        if (outcome.Status == SagaItemOutcomeStatus.Failed)
        {
            await AppendAuditAsync(
                AuditEventTypes.ReconciliationMismatchDetected,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                "Local Failed but provider Applied — correcting local to Succeeded",
                correlationId,
                cancellationToken);
            saga.CorrectItemSucceededFromReconciliation(outcome.LogicalOperationId, correlationId);
        }
        else if (outcome.Status != SagaItemOutcomeStatus.Succeeded)
        {
            saga.CorrectItemSucceededFromReconciliation(outcome.LogicalOperationId, correlationId);
        }

        await _localOutcomes.TryRecordAsync(
            new LocalFulfillmentOutcome(
                request.Id.Value,
                requestItem.Id.Value,
                outcome.LogicalOperationId.Value,
                requestItem.AttemptId.Value,
                requestItem.EntitlementId.Value,
                providerName,
                Succeeded: true,
                ErrorCode: null,
                ErrorMessage: null,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await AppendAuditAsync(
            AuditEventTypes.ReconciliationItemRepairedSuccess,
            request.Id,
            outcome.LogicalOperationId,
            actor,
            status.Detail ?? "Provider confirmed Applied; local repaired to Succeeded",
            correlationId,
            cancellationToken);

        return new ReconciliationItemResult(
            outcome.LogicalOperationId.Value,
            providerName,
            ReconciliationItemAction.RepairedSuccess,
            status.Detail ?? "Repaired to Succeeded from provider Applied.");
    }

    public async Task<ReconciliationItemResult> ApplyConfirmedFailureAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        LicenseExtensionRequestItem requestItem,
        SagaItemOutcome outcome,
        ProviderOperationStatus status,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var providerName = requestItem.Provider.ToString();

        // Forward recovery: never demote a local Succeeded.
        if (outcome.Status == SagaItemOutcomeStatus.Succeeded)
        {
            await AppendAuditAsync(
                AuditEventTypes.ReconciliationMismatchDetected,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                "Local Succeeded but provider Failed — keeping local Succeeded (forward recovery)",
                correlationId,
                cancellationToken);
            return new ReconciliationItemResult(
                outcome.LogicalOperationId.Value,
                providerName,
                ReconciliationItemAction.MismatchNoted,
                "Local success retained despite provider Failed record.");
        }

        if (outcome.Status != SagaItemOutcomeStatus.Failed)
        {
            saga.RecordItemFailed(
                outcome.LogicalOperationId,
                status.FailureReason ?? "PROVIDER_FAILED",
                status.Detail ?? "Provider confirmed definitive failure",
                correlationId);
        }

        await _localOutcomes.TryRecordAsync(
            new LocalFulfillmentOutcome(
                request.Id.Value,
                requestItem.Id.Value,
                outcome.LogicalOperationId.Value,
                requestItem.AttemptId.Value,
                requestItem.EntitlementId.Value,
                providerName,
                Succeeded: false,
                ErrorCode: status.FailureReason ?? "PROVIDER_FAILED",
                ErrorMessage: status.Detail,
                DateTimeOffset.UtcNow),
            cancellationToken);

        await AppendAuditAsync(
            AuditEventTypes.ReconciliationItemMarkedFailed,
            request.Id,
            outcome.LogicalOperationId,
            actor,
            "Provider confirmed failure; marked Failed (retry-eligible)",
            correlationId,
            cancellationToken);

        return new ReconciliationItemResult(
            outcome.LogicalOperationId.Value,
            providerName,
            ReconciliationItemAction.MarkedFailedRetryEligible,
            status.Detail ?? "Marked Failed; selective retry eligible.");
    }

    public async Task<ReconciliationItemResult> KeepUnknownAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        SagaItemOutcome outcome,
        ProviderOperationStatus status,
        string actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var detail = status.Detail
            ?? (status.Kind == ProviderOperationLookupKind.NotFound
                ? "Provider has no AppliedLicenseOperation; left Uncertain (no blind retry)"
                : "Provider status unknown; left Uncertain (no blind retry)");

        // Age out Uncertain/Pending unknowns so operators are not wedged forever (M5).
        if (outcome.Status is SagaItemOutcomeStatus.Pending or SagaItemOutcomeStatus.Uncertain
            && ShouldAgeUncertainToFailed(outcome))
        {
            var agedDetail =
                $"Uncertain aged out after {_aging.UncertainMaxAgeDays}d without provider confirmation; marked Failed (retry-eligible). Prior: {detail}";
            saga.RecordItemFailed(
                outcome.LogicalOperationId,
                "UncertainAgedOut",
                agedDetail,
                correlationId);

            await AppendAuditAsync(
                AuditEventTypes.ReconciliationItemMarkedFailed,
                request.Id,
                outcome.LogicalOperationId,
                actor,
                agedDetail,
                correlationId,
                cancellationToken);

            return new ReconciliationItemResult(
                outcome.LogicalOperationId.Value,
                request.Items.First(i => i.LogicalOperationId.Equals(outcome.LogicalOperationId))
                    .Provider.ToString(),
                ReconciliationItemAction.MarkedFailedRetryEligible,
                agedDetail);
        }

        // Do not demote Succeeded; do not invent Failed on fresh NotFound.
        if (outcome.Status is SagaItemOutcomeStatus.Pending or SagaItemOutcomeStatus.Uncertain)
        {
            saga.KeepItemUncertainFromReconciliation(
                outcome.LogicalOperationId,
                detail,
                correlationId);
        }

        await AppendAuditAsync(
            AuditEventTypes.ReconciliationItemStillUnknown,
            request.Id,
            outcome.LogicalOperationId,
            actor,
            detail,
            correlationId,
            cancellationToken);

        return new ReconciliationItemResult(
            outcome.LogicalOperationId.Value,
            status.Provider.ToString(),
            ReconciliationItemAction.KeptUnknown,
            detail);
    }

    public Task SyncRequestStatusAsync(
        LicenseExtensionRequest request,
        LicenseExtensionSaga saga,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (saga.State is not (
            SagaState.Completed or
            SagaState.Failed or
            SagaState.PartiallyCompleted))
        {
            // Still reconciling — leave request as-is (may already be PartiallyFulfilled).
            return Task.CompletedTask;
        }

        if (request.Status is not (
            LicenseExtensionRequestStatus.InFulfillment or
            LicenseExtensionRequestStatus.PartiallyFulfilled or
            LicenseExtensionRequestStatus.Failed))
        {
            return Task.CompletedTask;
        }

        try
        {
            request.CompleteFulfillment(saga.SucceededItemCount, saga.FailedItemCount);
        }
        catch (Domain.Common.DomainException ex)
        {
            _logger.LogDebug(ex, "Request status already settled for {RequestId}", request.Id.Value);
        }

        return Task.CompletedTask;
    }

    public Task AppendAuditAsync(
        string eventType,
        RequestId requestId,
        LogicalOperationId? logicalOperationId,
        string actor,
        string detail,
        string? correlationId,
        CancellationToken cancellationToken) =>
        _audit.AppendAsync(
            new AuditEventRecord(
                Guid.NewGuid(),
                eventType,
                requestId,
                logicalOperationId,
                actor,
                detail,
                correlationId,
                DateTimeOffset.UtcNow),
            cancellationToken);
}
