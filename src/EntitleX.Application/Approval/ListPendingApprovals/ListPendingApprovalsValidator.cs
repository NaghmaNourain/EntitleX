using FluentValidation;

namespace EntitleX.Application.Approval.ListPendingApprovals;

public sealed class ListPendingApprovalsValidator : AbstractValidator<ListPendingApprovalsQuery>
{
    public ListPendingApprovalsValidator()
    {
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
