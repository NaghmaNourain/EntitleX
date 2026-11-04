using MediatR;

namespace EntitleX.Application.Ops.GetOpsSummary;

public sealed record GetOpsSummaryQuery(int Take = 25) : IRequest<OpsSummaryDto>;

public sealed record OpsSummaryDto(
    DateTimeOffset GeneratedAtUtc,
    int PendingApprovalCount,
    int ReconciliationRequiredCount,
    IReadOnlyList<PendingApprovalOpsItemDto> PendingApprovals,
    IReadOnlyList<ReconciliationOpsItemDto> ReconciliationRequired);

public sealed record PendingApprovalOpsItemDto(
    Guid RequestId,
    string Band,
    string? CurrentStageRole,
    DateTimeOffset CreatedAtUtc);

public sealed record ReconciliationOpsItemDto(
    Guid RequestId,
    string SagaState,
    int SucceededItemCount,
    int FailedItemCount,
    int PendingItemCount,
    DateTimeOffset UpdatedAtUtc);
