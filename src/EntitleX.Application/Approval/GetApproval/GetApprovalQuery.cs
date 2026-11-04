using EntitleX.Application.Approval.Models;
using MediatR;

namespace EntitleX.Application.Approval.GetApproval;

public sealed record GetApprovalQuery(Guid RequestId) : IRequest<ApprovalCaseDto>;
