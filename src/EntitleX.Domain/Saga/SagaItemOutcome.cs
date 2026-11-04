using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Saga;

/// <summary>
/// Saga-owned item outcome. Successful extensions are never compensated when
/// siblings fail (forward recovery).
/// </summary>
public sealed class SagaItemOutcome
{
    private SagaItemOutcome()
    {
        // EF Core
        EntitlementId = default;
        LogicalOperationId = default;
    }

    private SagaItemOutcome(
        Guid id,
        RequestItemId requestItemId,
        EntitlementId entitlementId,
        LogicalOperationId logicalOperationId,
        SagaItemOutcomeStatus status,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        RequestItemId = requestItemId;
        EntitlementId = entitlementId;
        LogicalOperationId = logicalOperationId;
        Status = status;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public RequestItemId RequestItemId { get; private set; }

    public EntitlementId EntitlementId { get; private set; }

    public LogicalOperationId LogicalOperationId { get; private set; }

    public SagaItemOutcomeStatus Status { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    internal static SagaItemOutcome CreatePending(
        RequestItemId requestItemId,
        EntitlementId entitlementId,
        LogicalOperationId logicalOperationId,
        DateTimeOffset atUtc) =>
        new(
            Guid.NewGuid(),
            requestItemId,
            entitlementId,
            logicalOperationId,
            SagaItemOutcomeStatus.Pending,
            atUtc);

    internal void MarkSucceeded(DateTimeOffset atUtc)
    {
        if (Status == SagaItemOutcomeStatus.Succeeded)
        {
            return;
        }

        if (Status is not (SagaItemOutcomeStatus.Pending or SagaItemOutcomeStatus.Uncertain))
        {
            throw new DomainException(
                $"Cannot mark item '{RequestItemId}' succeeded from outcome '{Status}'.");
        }

        Status = SagaItemOutcomeStatus.Succeeded;
        ErrorCode = null;
        ErrorMessage = null;
        UpdatedAtUtc = atUtc;
    }

    internal void MarkFailed(string? errorCode, string? errorMessage, DateTimeOffset atUtc)
    {
        if (Status == SagaItemOutcomeStatus.Succeeded)
        {
            throw new DomainException(
                $"Cannot fail item '{RequestItemId}' after a successful extension (forward recovery).");
        }

        if (Status == SagaItemOutcomeStatus.Failed)
        {
            return;
        }

        Status = SagaItemOutcomeStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAtUtc = atUtc;
    }

    internal void MarkUncertain(string? reason, DateTimeOffset atUtc)
    {
        if (Status is SagaItemOutcomeStatus.Succeeded or SagaItemOutcomeStatus.Failed)
        {
            return;
        }

        Status = SagaItemOutcomeStatus.Uncertain;
        ErrorCode = "UNCERTAIN";
        ErrorMessage = reason;
        UpdatedAtUtc = atUtc;
    }
}
