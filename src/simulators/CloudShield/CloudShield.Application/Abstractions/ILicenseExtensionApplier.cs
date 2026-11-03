using CloudShield.Application.Models;

namespace CloudShield.Application.Abstractions;

/// <summary>
/// Atomically claims a LogicalOperationId and updates entitlement expiry
/// inside a single DB transaction (no remote HTTP inside the transaction).
/// </summary>
public interface ILicenseExtensionApplier
{
    Task<LicenseOperationDto> ApplyAsync(
        string entitlementKey,
        SubmitLicenseExtensionRequest request,
        CancellationToken cancellationToken = default);
}
