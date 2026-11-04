using NetworkGuard.Application.Models;

namespace NetworkGuard.Application.Abstractions;

public interface ILicenseExtensionApplier
{
    Task<LicenseOperationDto> ApplyAsync(
        string licenseId,
        SubmitLicenseExtensionRequest request,
        CancellationToken cancellationToken = default);
}
