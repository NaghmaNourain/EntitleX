using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Resource-level authorization for request-scoped reads and entitlement search.
/// Complements ASP.NET endpoint policies (role gates) with ownership / privileged-role checks.
/// </summary>
public interface IResourceAuthorizationService
{
    /// <summary>
    /// Ensures the caller may read the given request (and related approval/saga/audit).
    /// Privileged roles (PlatformAdmin, DealDeskManager, RenewalManager) may read any request;
    /// others only when <see cref="LicenseExtensionRequest.RequestedBy"/> matches the caller.
    /// </summary>
    void EnsureCanReadRequest(LicenseExtensionRequest request);

    /// <summary>
    /// Loads the request by id (404 if missing) then applies <see cref="EnsureCanReadRequest"/>.
    /// </summary>
    Task EnsureCanReadRequestAsync(RequestId requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Entitlement search may harvest across customers — only PlatformAdmin / DealDeskManager.
    /// </summary>
    void EnsureCanSearchEntitlements();
}
