using FluentValidation;

namespace EntitleX.Application.Approval.ListPendingApprovals;

public sealed class ListPendingApprovalsValidator : AbstractValidator<ListPendingApprovalsQuery>
{
    public ListPendingApprovalsValidator()
    {
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
    }
}
