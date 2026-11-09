using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Identity;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Authorization;

public sealed class ResourceAuthorizationService : IResourceAuthorizationService
{
    private readonly ICurrentUser _currentUser;
    private readonly ILicenseExtensionRequestRepository _requests;

    public ResourceAuthorizationService(
        ICurrentUser currentUser,
        ILicenseExtensionRequestRepository requests)
    {
        _currentUser = currentUser;
        _requests = requests;
    }

    public void EnsureCanReadRequest(LicenseExtensionRequest request)
    {
        EnsureAuthenticated();

        if (HasUnrestrictedRequestRead())
        {
            return;
        }

        if (IsRequestOwner(request))
        {
            return;
        }

        throw new ForbiddenException(
            "You are not allowed to access this license-extension request.");
    }

    public async Task EnsureCanReadRequestAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetByIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(
                $"License-extension request '{requestId.Value}' was not found.");

        EnsureCanReadRequest(request);
    }

    public void EnsureCanSearchEntitlements()
    {
        EnsureAuthenticated();

        if (_currentUser.IsInRole(EntitleXRoles.PlatformAdmin)
            || _currentUser.IsInRole(EntitleXRoles.DealDeskManager))
        {
            return;
        }

        throw new ForbiddenException(
            "Entitlement search across customers requires PlatformAdmin or DealDeskManager.");
    }

    private void EnsureAuthenticated()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new ForbiddenException("Authentication is required.");
        }
    }

    private bool HasUnrestrictedRequestRead() =>
        _currentUser.IsInRole(EntitleXRoles.PlatformAdmin)
        || _currentUser.IsInRole(EntitleXRoles.DealDeskManager)
        || _currentUser.IsInRole(EntitleXRoles.RenewalManager);

    private bool IsRequestOwner(LicenseExtensionRequest request) =>
        !string.IsNullOrWhiteSpace(_currentUser.UserId)
        && string.Equals(
            request.RequestedBy,
            _currentUser.UserId,
            StringComparison.Ordinal);
}
