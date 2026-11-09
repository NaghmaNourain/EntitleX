using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// After final approval, transitions the request into fulfillment and writes
/// provider (or legacy SQS) commands into the transactional outbox.
/// </summary>
public interface IFulfillmentOutboxService
{
    /// <returns>
    /// <c>true</c> when provider Kafka commands were written directly (saga may
    /// advance to Processing immediately); <c>false</c> for the legacy SQS hop.
    /// </returns>
    Task<bool> EnqueueAfterFinalApprovalAsync(
        LicenseExtensionRequest request,
        Guid? correlationId = null,
        Guid? causationId = null,
        CancellationToken cancellationToken = default);
}
