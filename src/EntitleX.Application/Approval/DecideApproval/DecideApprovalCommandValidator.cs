using FluentValidation;

namespace EntitleX.Application.Approval.DecideApproval;

public sealed class DecideApprovalCommandValidator : AbstractValidator<DecideApprovalCommand>
{
    public DecideApprovalCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Outcome)
            .NotEmpty()
            .Must(o => o.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                       || o.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Outcome must be 'Approved' or 'Rejected'.");
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}
