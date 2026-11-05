using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// After final approval, transitions the request into fulfillment and writes the
/// fulfillment command into the transactional outbox.
/// </summary>
public interface IFulfillmentOutboxService
{
    Task EnqueueAfterFinalApprovalAsync(
        LicenseExtensionRequest request,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default);
}
