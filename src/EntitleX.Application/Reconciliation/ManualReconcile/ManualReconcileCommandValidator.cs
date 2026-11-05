using FluentValidation;

namespace EntitleX.Application.Reconciliation.ManualReconcile;

public sealed class ManualReconcileCommandValidator : AbstractValidator<ManualReconcileCommand>
{
    public ManualReconcileCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
    }
}
