using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Common;
using EntitleX.Domain.Saga;

namespace EntitleX.Domain.LicenseExtension;

/// <summary>
/// Selects eligible failed items from a source request for selective retry.
/// Succeeded items are never eligible (forward recovery).
/// </summary>
public static class FailedItemSelector
{
    public static IReadOnlyList<LicenseExtensionRequestItem> SelectEligibleFailedItems(
        LicenseExtensionRequest source,
        LicenseExtensionSaga saga)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(saga);

        if (!saga.RequestId.Equals(source.Id))
        {
            throw new DomainException(
                $"Saga request '{saga.RequestId}' does not match source '{source.Id}'.");
        }

        var failedLogicalIds = saga.ItemOutcomes
            .Where(o => o.Status == SagaItemOutcomeStatus.Failed)
            .Select(o => o.LogicalOperationId)
            .ToHashSet();

        if (failedLogicalIds.Count == 0)
        {
            throw new DomainException(
                $"Source request '{source.Id}' has no failed items eligible for selective retry.");
        }

        var selected = source.Items
            .Where(i => failedLogicalIds.Contains(i.LogicalOperationId))
            .ToList();

        if (selected.Count == 0)
        {
            throw new DomainException(
                $"Source request '{source.Id}' saga reports failures that do not match request items.");
        }

        return selected;
    }

    /// <summary>
    /// Optional filter: when <paramref name="logicalOperationIds"/> is provided,
    /// only those failed items are selected (must all be failed and present).
    /// </summary>
    public static IReadOnlyList<LicenseExtensionRequestItem> SelectEligibleFailedItems(
        LicenseExtensionRequest source,
        LicenseExtensionSaga saga,
        IReadOnlyCollection<LogicalOperationId>? logicalOperationIds)
    {
        var allFailed = SelectEligibleFailedItems(source, saga);
        if (logicalOperationIds is null || logicalOperationIds.Count == 0)
        {
            return allFailed;
        }

        var requested = logicalOperationIds.ToHashSet();
        var selected = allFailed
            .Where(i => requested.Contains(i.LogicalOperationId))
            .ToList();

        if (selected.Count != requested.Count)
        {
            var missing = requested
                .Where(id => selected.All(i => !i.LogicalOperationId.Equals(id)))
                .Select(id => id.Value)
                .ToArray();
            throw new DomainException(
                "One or more requested LogicalOperationIds are not eligible failed items: " +
                string.Join(", ", missing));
        }

        return selected;
    }
}
