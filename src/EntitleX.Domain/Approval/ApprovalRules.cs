using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.Approval;

/// <summary>
/// Day-band approval policy:
/// 1–30 auto-approve; 31–90 Renewal Manager; 91–365 RM then Deal Desk; &gt;365 unsupported.
/// </summary>
public static class ApprovalRules
{
    public const int MinSupportedDays = 1;
    public const int AutoApproveMaxDays = 30;
    public const int RenewalManagerMaxDays = 90;
    public const int MaxSupportedDays = 365;

    /// <summary>
    /// Computes the controlling extension length as the maximum whole-day span
    /// across request items (requested expiry minus current expiry baseline).
    /// </summary>
    public static int ComputeMaxExtensionDays(IEnumerable<LicenseExtensionRequestItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items as IList<LicenseExtensionRequestItem> ?? items.ToList();
        if (list.Count == 0)
        {
            throw new DomainException("Cannot evaluate approval rules without request items.");
        }

        return list.Max(ComputeExtensionDays);
    }

    public static int ComputeExtensionDays(LicenseExtensionRequestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var span = item.RequestedExpiryUtc - item.CurrentExpiryUtc;
        var days = (int)Math.Ceiling(span.TotalDays);
        return Math.Max(days, 0);
    }

    public static ApprovalBand EvaluateBand(int maxExtensionDays)
    {
        if (maxExtensionDays < MinSupportedDays)
        {
            throw new DomainException(
                $"Extension length must be at least {MinSupportedDays} day(s); got {maxExtensionDays}.");
        }

        if (maxExtensionDays <= AutoApproveMaxDays)
        {
            return ApprovalBand.AutoApprove;
        }

        if (maxExtensionDays <= RenewalManagerMaxDays)
        {
            return ApprovalBand.RenewalManagerOnly;
        }

        if (maxExtensionDays <= MaxSupportedDays)
        {
            return ApprovalBand.RenewalManagerThenDealDesk;
        }

        return ApprovalBand.Unsupported;
    }

    public static ApprovalBand EvaluateBand(IEnumerable<LicenseExtensionRequestItem> items) =>
        EvaluateBand(ComputeMaxExtensionDays(items));

    /// <summary>Ordered human stages required for the band (empty for auto / unsupported).</summary>
    public static IReadOnlyList<ApprovalStageRole> RequiredStages(ApprovalBand band) =>
        band switch
        {
            ApprovalBand.AutoApprove => [],
            ApprovalBand.RenewalManagerOnly => [ApprovalStageRole.RenewalManager],
            ApprovalBand.RenewalManagerThenDealDesk =>
            [
                ApprovalStageRole.RenewalManager,
                ApprovalStageRole.DealDeskManager
            ],
            ApprovalBand.Unsupported => [],
            _ => throw new DomainException($"Unknown approval band '{band}'.")
        };

    public static string RoleName(ApprovalStageRole stageRole) =>
        stageRole switch
        {
            ApprovalStageRole.RenewalManager => Identity.EntitleXRoles.RenewalManager,
            ApprovalStageRole.DealDeskManager => Identity.EntitleXRoles.DealDeskManager,
            _ => throw new DomainException($"Unknown approval stage role '{stageRole}'.")
        };
}
