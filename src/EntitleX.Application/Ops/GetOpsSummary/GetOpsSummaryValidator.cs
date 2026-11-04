using FluentValidation;

namespace EntitleX.Application.Ops.GetOpsSummary;

public sealed class GetOpsSummaryValidator : AbstractValidator<GetOpsSummaryQuery>
{
    public GetOpsSummaryValidator()
    {
        RuleFor(x => x.Take).InclusiveBetween(1, 100);
    }
}
