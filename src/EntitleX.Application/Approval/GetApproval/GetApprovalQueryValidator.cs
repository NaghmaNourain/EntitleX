using FluentValidation;

namespace EntitleX.Application.Approval.GetApproval;

public sealed class GetApprovalQueryValidator : AbstractValidator<GetApprovalQuery>
{
    public GetApprovalQueryValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
    }
}
