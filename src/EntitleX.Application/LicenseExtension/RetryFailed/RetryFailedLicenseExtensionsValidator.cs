using FluentValidation;

namespace EntitleX.Application.LicenseExtension.RetryFailed;

public sealed class RetryFailedLicenseExtensionsValidator
    : AbstractValidator<RetryFailedLicenseExtensionsCommand>
{
    public RetryFailedLicenseExtensionsValidator()
    {
        RuleFor(x => x.SourceRequestId)
            .NotEmpty();

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(128)
            .Matches(@"^[A-Za-z0-9._:-]+$")
            .WithMessage("IdempotencyKey must be 1–128 characters of [A-Za-z0-9._:-].");

        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .When(x => x.Notes is not null);

        RuleForEach(x => x.LogicalOperationIds!)
            .NotEmpty()
            .When(x => x.LogicalOperationIds is not null);
    }
}
