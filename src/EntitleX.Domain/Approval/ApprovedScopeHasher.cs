using System.Security.Cryptography;
using System.Text;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Domain.Approval;

/// <summary>
/// Stable hash of the approved item scope for later reuse / idempotent fulfillment.
/// </summary>
public static class ApprovedScopeHasher
{
    public static string Compute(LicenseExtensionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        builder.Append(request.Id.Value.ToString("D"));
        builder.Append('|');
        AppendItems(builder, request.Items);

        return Hash(builder.ToString());
    }

    /// <summary>
    /// Item-only fingerprint (no request id) used when comparing approved scopes
    /// across selective-retry generations that share <c>LogicalOperationId</c>s.
    /// </summary>
    public static string ComputeItemScope(IEnumerable<LicenseExtensionRequestItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = new StringBuilder();
        AppendItems(builder, items);
        return Hash(builder.ToString());
    }

    private static void AppendItems(StringBuilder builder, IEnumerable<LicenseExtensionRequestItem> items)
    {
        foreach (var item in items
                     .OrderBy(i => i.EntitlementId.Value, StringComparer.Ordinal)
                     .ThenBy(i => i.Provider.ToString(), StringComparer.Ordinal)
                     .ThenBy(i => i.LogicalOperationId.Value))
        {
            builder.Append(item.EntitlementId.Value);
            builder.Append(':');
            builder.Append(item.Provider);
            builder.Append(':');
            builder.Append(item.CurrentExpiryUtc.UtcTicks);
            builder.Append(':');
            builder.Append(item.RequestedExpiryUtc.UtcTicks);
            builder.Append(':');
            builder.Append(item.LogicalOperationId.Value.ToString("D"));
            builder.Append(';');
        }
    }

    private static string Hash(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
