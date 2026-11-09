using EntitleX.Application.LicenseExtension.Models;
using MediatR;

namespace EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;

/// <summary>
/// Creates a license-extension request. Actor (<c>RequestedBy</c>) is bound server-side
/// from <see cref="Abstractions.ICurrentUser"/> — client-supplied actor fields are ignored.
/// </summary>
public sealed record CreateLicenseExtensionRequestCommand(
    string? ExtensionNumber,
    IReadOnlyList<CreateLicenseExtensionRequestItemDto> Items) : IRequest<LicenseExtensionRequestDto>;

public sealed record CreateLicenseExtensionRequestItemDto(
    string EntitlementId,
    string Provider,
    DateTimeOffset CurrentExpiryUtc,
    DateTimeOffset RequestedExpiryUtc);
