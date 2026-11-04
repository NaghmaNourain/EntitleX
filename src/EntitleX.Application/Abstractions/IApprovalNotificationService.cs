using EntitleX.Application.EntitlementAggregation.Models;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Abstractions;

public interface IApprovalNotificationService
{
    Task NotifyApprovalStartedAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        CancellationToken cancellationToken = default);

    Task NotifyStageReadyAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalStage stage,
        CancellationToken cancellationToken = default);

    Task NotifyDecisionRecordedAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>Reminds the role responsible for the current pending stage (≥ threshold days).</summary>
    Task NotifyApprovalReminderAsync(
        LicenseExtensionRequest request,
        ApprovalCase approvalCase,
        ApprovalStage stage,
        TimeSpan pendingFor,
        CancellationToken cancellationToken = default);

    /// <summary>Notifies Renewal Representatives about an entitlement nearing expiry.</summary>
    Task NotifyExpirationWarningAsync(
        NormalizedEntitlementDto entitlement,
        string portalLink,
        CancellationToken cancellationToken = default);
}
