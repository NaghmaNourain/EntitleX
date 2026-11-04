using FluentValidation;

namespace EntitleX.Application.Saga.GetSaga;

public sealed class GetSagaQueryValidator : AbstractValidator<GetSagaQuery>
{
    public GetSagaQueryValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
    }
}
