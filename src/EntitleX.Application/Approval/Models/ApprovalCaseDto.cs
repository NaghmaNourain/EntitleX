using EntitleX.Domain.Approval;

namespace EntitleX.Application.Approval.Models;

public sealed record ApprovalCaseDto(
    Guid Id,
    Guid RequestId,
    int MaxExtensionDays,
    string Band,
    string Status,
    int CurrentStageOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<ApprovalStageDto> Stages,
    IReadOnlyList<ApprovalDecisionDto> Decisions);

public sealed record ApprovalStageDto(
    Guid Id,
    int Order,
    string RequiredRole,
    string Status,
    Guid? DecisionId);

public sealed record ApprovalDecisionDto(
    Guid Id,
    int StageOrder,
    string StageRole,
    string Outcome,
    string ActorUserId,
    string ActorRole,
    string ActorDisplayName,
    string? Comment,
    string? CorrelationId,
    DateTimeOffset DecidedAtUtc);

public static class ApprovalCaseMappings
{
    public static ApprovalCaseDto ToDto(this ApprovalCase approvalCase) =>
        new(
            approvalCase.Id,
            approvalCase.RequestId.Value,
            approvalCase.MaxExtensionDays,
            approvalCase.Band.ToString(),
            approvalCase.Status.ToString(),
            approvalCase.CurrentStageOrder,
            approvalCase.CreatedAtUtc,
            approvalCase.CompletedAtUtc,
            approvalCase.Stages.Select(s => new ApprovalStageDto(
                s.Id,
                s.Order,
                ApprovalRules.RoleName(s.RequiredRole),
                s.Status.ToString(),
                s.DecisionId)).ToList(),
            approvalCase.Decisions.Select(d => new ApprovalDecisionDto(
                d.Id,
                d.StageOrder,
                ApprovalRules.RoleName(d.StageRole),
                d.Outcome.ToString(),
                d.ActorUserId,
                d.ActorRole,
                d.ActorDisplayName,
                d.Comment,
                d.CorrelationId,
                d.DecidedAtUtc)).ToList());
}
