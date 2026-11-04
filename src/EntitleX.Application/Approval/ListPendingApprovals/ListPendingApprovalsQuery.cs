using MediatR;

namespace EntitleX.Application.Approval.ListPendingApprovals;

public sealed record ListPendingApprovalsQuery(
    string? Role = null,
    int Take = 50,
    int Skip = 0) : IRequest<PendingApprovalsPageDto>;

public sealed record PendingApprovalsPageDto(
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<PendingApprovalSummaryDto> Items);

public sealed record PendingApprovalSummaryDto(
    Guid ApprovalCaseId,
    Guid RequestId,
    int MaxExtensionDays,
    string Band,
    string Status,
    int CurrentStageOrder,
    string? CurrentStageRole,
    DateTimeOffset CreatedAtUtc);
