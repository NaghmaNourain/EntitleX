using EntitleX.Application.LicenseExtension.Models;
using MediatR;

namespace EntitleX.Application.LicenseExtension.GetLicenseExtensionRequest;

public sealed record GetLicenseExtensionRequestQuery(Guid Id) : IRequest<LicenseExtensionRequestDto>;
