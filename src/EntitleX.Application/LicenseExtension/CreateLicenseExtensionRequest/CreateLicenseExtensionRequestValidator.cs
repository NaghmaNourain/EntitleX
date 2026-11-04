using EntitleX.Domain.LicenseExtension;
using FluentValidation;

namespace EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;

public sealed class CreateLicenseExtensionRequestValidator
    : AbstractValidator<CreateLicenseExtensionRequestCommand>
{
    public CreateLicenseExtensionRequestValidator()
    {
        RuleFor(x => x.RequestedBy)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.ExtensionNumber)
            .MaximumLength(64)
            .When(x => x.ExtensionNumber is not null);

        RuleFor(x => x.Items)
            .NotEmpty()
            .WithMessage("At least one item is required.")
            .Must(items => items.Count <= LicenseExtensionRequest.MaxItems)
            .WithMessage($"A request cannot contain more than {LicenseExtensionRequest.MaxItems} items.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.EntitlementId)
                .NotEmpty()
                .MaximumLength(128);

            item.RuleFor(i => i.Provider)
                .NotEmpty()
                .Must(BeKnownProvider)
                .WithMessage("Provider must be CloudShield or NetworkGuard.");

            item.RuleFor(i => i.RequestedExpiryUtc)
                .GreaterThan(i => i.CurrentExpiryUtc)
                .WithMessage("RequestedExpiryUtc must be after CurrentExpiryUtc.");
        });
    }

    private static bool BeKnownProvider(string provider) =>
        Enum.TryParse<ProviderSystem>(provider, ignoreCase: true, out _);
}
