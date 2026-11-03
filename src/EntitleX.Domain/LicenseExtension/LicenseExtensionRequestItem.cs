using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.LicenseExtension;

/// <summary>Line item within a <see cref="LicenseExtensionRequest"/> aggregate.</summary>
public sealed class LicenseExtensionRequestItem
{
    private LicenseExtensionRequestItem()
    {
        // EF Core
        EntitlementId = default;
        LogicalOperationId = default;
        AttemptId = default;
    }

    private LicenseExtensionRequestItem(
        RequestItemId id,
        EntitlementId entitlementId,
        ProviderSystem provider,
        LogicalOperationId logicalOperationId,
        AttemptId attemptId,
        DateTimeOffset currentExpiryUtc,
        DateTimeOffset requestedExpiryUtc)
    {
        Id = id;
        EntitlementId = entitlementId;
        Provider = provider;
        LogicalOperationId = logicalOperationId;
        AttemptId = attemptId;
        CurrentExpiryUtc = currentExpiryUtc;
        RequestedExpiryUtc = requestedExpiryUtc;
    }

    public RequestItemId Id { get; private set; }

    public EntitlementId EntitlementId { get; private set; }

    public ProviderSystem Provider { get; private set; }

    public LogicalOperationId LogicalOperationId { get; private set; }

    public AttemptId AttemptId { get; private set; }

    /// <summary>Expiry baseline captured when the item was added.</summary>
    public DateTimeOffset CurrentExpiryUtc { get; private set; }

    public DateTimeOffset RequestedExpiryUtc { get; private set; }

    internal static LicenseExtensionRequestItem Create(
        EntitlementId entitlementId,
        ProviderSystem provider,
        DateTimeOffset currentExpiryUtc,
        DateTimeOffset requestedExpiryUtc,
        LogicalOperationId? logicalOperationId = null,
        AttemptId? attemptId = null)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new DomainException($"Unknown provider system '{provider}'.");
        }

        ExpiryBaselineRules.EnsureRequestedExpiryAfterBaseline(currentExpiryUtc, requestedExpiryUtc);

        return new LicenseExtensionRequestItem(
            RequestItemId.New(),
            entitlementId,
            provider,
            logicalOperationId ?? LogicalOperationId.New(),
            attemptId ?? AttemptId.New(),
            currentExpiryUtc,
            requestedExpiryUtc);
    }
}
