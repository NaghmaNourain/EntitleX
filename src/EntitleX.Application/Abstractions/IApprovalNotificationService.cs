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
}
