using EntitleX.Application.LicenseExtension.Models;
using MediatR;

namespace EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;

public sealed record CreateLicenseExtensionRequestCommand(
    string RequestedBy,
    string? ExtensionNumber,
    IReadOnlyList<CreateLicenseExtensionRequestItemDto> Items) : IRequest<LicenseExtensionRequestDto>;

public sealed record CreateLicenseExtensionRequestItemDto(
    string EntitlementId,
    string Provider,
    DateTimeOffset CurrentExpiryUtc,
    DateTimeOffset RequestedExpiryUtc);
