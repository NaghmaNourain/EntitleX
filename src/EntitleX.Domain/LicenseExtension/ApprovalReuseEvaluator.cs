using EntitleX.Domain.Approval;

namespace EntitleX.Domain.LicenseExtension;

/// <summary>
/// Determines whether a selective retry may reuse the parent's approval
/// because the <see cref="LicenseExtensionRequest.ApprovedScopeHash"/> is still valid
/// for the failed items being retried.
/// </summary>
public static class ApprovalReuseEvaluator
{
    public static bool CanReuseParentApproval(
        LicenseExtensionRequest parent,
        IReadOnlyList<LicenseExtensionRequestItem> failedItemsToRetry)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(failedItemsToRetry);

        if (string.IsNullOrWhiteSpace(parent.ApprovedScopeHash))
        {
            return false;
        }

        // Parent scope must be unchanged since approval.
        if (!string.Equals(
                parent.ApprovedScopeHash,
                ApprovedScopeHasher.Compute(parent),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (failedItemsToRetry.Count == 0)
        {
            return false;
        }

        var parentByLogical = parent.Items.ToDictionary(i => i.LogicalOperationId);

        foreach (var item in failedItemsToRetry)
        {
            if (!parentByLogical.TryGetValue(item.LogicalOperationId, out var parentItem))
            {
                return false;
            }

            if (!ItemsMatchApprovedScope(parentItem, item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemsMatchApprovedScope(
        LicenseExtensionRequestItem approved,
        LicenseExtensionRequestItem candidate) =>
        approved.EntitlementId.Equals(candidate.EntitlementId)
        && approved.Provider == candidate.Provider
        && approved.CurrentExpiryUtc == candidate.CurrentExpiryUtc
        && approved.RequestedExpiryUtc == candidate.RequestedExpiryUtc
        && approved.LogicalOperationId.Equals(candidate.LogicalOperationId);
}
