using FluentValidation;

namespace EntitleX.Application.Approval.StartApproval;

public sealed class StartApprovalCommandValidator : AbstractValidator<StartApprovalCommand>
{
    public StartApprovalCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
    }
}
