using EntitleX.Application.Approval.Models;
using MediatR;

namespace EntitleX.Application.Approval.StartApproval;

public sealed record StartApprovalCommand(
    Guid RequestId,
    string? CorrelationId = null) : IRequest<ApprovalCaseDto>;
