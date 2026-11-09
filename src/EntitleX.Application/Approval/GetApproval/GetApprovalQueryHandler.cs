using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.Models;
using EntitleX.Application.Common.Exceptions;
using EntitleX.SharedKernel.Ids;
using MediatR;

namespace EntitleX.Application.Approval.GetApproval;

public sealed class GetApprovalQueryHandler : IRequestHandler<GetApprovalQuery, ApprovalCaseDto>
{
    private readonly IApprovalCaseRepository _approvalRepository;
    private readonly IResourceAuthorizationService _authorization;

    public GetApprovalQueryHandler(
        IApprovalCaseRepository approvalRepository,
        IResourceAuthorizationService authorization)
    {
        _approvalRepository = approvalRepository;
        _authorization = authorization;
    }

    public async Task<ApprovalCaseDto> Handle(
        GetApprovalQuery query,
        CancellationToken cancellationToken)
    {
        var requestId = RequestId.From(query.RequestId);
        await _authorization.EnsureCanReadRequestAsync(requestId, cancellationToken);

        var approvalCase = await _approvalRepository.GetByRequestIdAsync(
            requestId,
            cancellationToken);

        if (approvalCase is null)
        {
            throw new NotFoundException(
                $"Approval case for request '{query.RequestId}' was not found.");
        }

        return approvalCase.ToDto();
    }
}
