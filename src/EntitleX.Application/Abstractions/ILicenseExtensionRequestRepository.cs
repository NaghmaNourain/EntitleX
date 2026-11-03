using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Abstractions;

public interface ILicenseExtensionRequestRepository
{
    Task AddAsync(LicenseExtensionRequest request, CancellationToken cancellationToken = default);

    Task<LicenseExtensionRequest?> GetByIdAsync(RequestId id, CancellationToken cancellationToken = default);

    Task<bool> ExtensionNumberExistsAsync(string extensionNumber, CancellationToken cancellationToken = default);
}
