using FluentValidation;

namespace EntitleX.Application.EntitlementAggregation.SearchCustomerEntitlements;

public sealed class SearchCustomerEntitlementsValidator
    : AbstractValidator<SearchCustomerEntitlementsQuery>
{
    public SearchCustomerEntitlementsValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty()
            .MaximumLength(128);
    }
}
