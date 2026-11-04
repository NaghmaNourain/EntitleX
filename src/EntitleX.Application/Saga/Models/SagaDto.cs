using EntitleX.Domain.Saga;

namespace EntitleX.Application.Saga.Models;

public sealed record SagaDto(
    Guid Id,
    Guid RequestId,
    string State,
    string RequestStatusProjection,
    string ApprovalBand,
    bool RequiresDealDesk,
    bool RenewalManagerApproved,
    bool DealDeskApproved,
    string TimeoutKind,
    DateTimeOffset? TimeoutDeadlineUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int SucceededItemCount,
    int FailedItemCount,
    int PendingItemCount,
    IReadOnlyList<SagaTransitionDto> Transitions,
    IReadOnlyList<SagaItemOutcomeDto> ItemOutcomes);

public sealed record SagaTransitionDto(
    Guid Id,
    string FromState,
    string ToState,
    string Reason,
    string? Actor,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc);

public sealed record SagaItemOutcomeDto(
    Guid Id,
    Guid RequestItemId,
    string EntitlementId,
    Guid LogicalOperationId,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset UpdatedAtUtc);

public static class SagaMappings
{
    public static SagaDto ToDto(this LicenseExtensionSaga saga) =>
        new(
            saga.Id,
            saga.RequestId.Value,
            saga.State.ToString(),
            SagaRequestStatusMap.ToRequestStatus(saga.State).ToString(),
            saga.ApprovalBand.ToString(),
            saga.RequiresDealDesk,
            saga.RenewalManagerApproved,
            saga.DealDeskApproved,
            saga.TimeoutKind.ToString(),
            saga.TimeoutDeadlineUtc,
            saga.CreatedAtUtc,
            saga.UpdatedAtUtc,
            saga.SucceededItemCount,
            saga.FailedItemCount,
            saga.PendingItemCount,
            saga.Transitions
                .OrderBy(t => t.OccurredAtUtc)
                .Select(t => new SagaTransitionDto(
                    t.Id,
                    t.FromState.ToString(),
                    t.ToState.ToString(),
                    t.Reason,
                    t.Actor,
                    t.CorrelationId,
                    t.OccurredAtUtc))
                .ToList(),
            saga.ItemOutcomes
                .Select(i => new SagaItemOutcomeDto(
                    i.Id,
                    i.RequestItemId.Value,
                    i.EntitlementId.Value,
                    i.LogicalOperationId.Value,
                    i.Status.ToString(),
                    i.ErrorCode,
                    i.ErrorMessage,
                    i.UpdatedAtUtc))
                .ToList());
}
