using FluentValidation;

namespace EntitleX.Application.LicenseExtension.GetLicenseExtensionRequest;

public sealed class GetLicenseExtensionRequestValidator
    : AbstractValidator<GetLicenseExtensionRequestQuery>
{
    public GetLicenseExtensionRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();
    }
}
