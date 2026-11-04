using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.Models;
using EntitleX.Application.Common.Exceptions;
using EntitleX.BuildingBlocks.Ids;
using MediatR;

namespace EntitleX.Application.Approval.GetApproval;

public sealed class GetApprovalQueryHandler : IRequestHandler<GetApprovalQuery, ApprovalCaseDto>
{
    private readonly IApprovalCaseRepository _approvalRepository;

    public GetApprovalQueryHandler(IApprovalCaseRepository approvalRepository)
    {
        _approvalRepository = approvalRepository;
    }

    public async Task<ApprovalCaseDto> Handle(
        GetApprovalQuery query,
        CancellationToken cancellationToken)
    {
        var approvalCase = await _approvalRepository.GetByRequestIdAsync(
            RequestId.From(query.RequestId),
            cancellationToken);

        if (approvalCase is null)
        {
            throw new NotFoundException(
                $"Approval case for request '{query.RequestId}' was not found.");
        }

        return approvalCase.ToDto();
    }
}
