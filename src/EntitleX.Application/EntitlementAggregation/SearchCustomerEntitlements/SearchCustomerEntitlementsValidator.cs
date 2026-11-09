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
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        // 0 = unpaged (full aggregation); otherwise clamp 1..100 at the handler.
        RuleFor(x => x.PageSize).InclusiveBetween(0, 100);
    }
}
