using EntitleX.Application.Approval.Models;
using MediatR;

namespace EntitleX.Application.Approval.DecideApproval;

public sealed record DecideApprovalCommand(
    Guid RequestId,
    string Outcome,
    string? Comment = null,
    string? CorrelationId = null) : IRequest<ApprovalCaseDto>;
