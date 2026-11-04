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

        foreach (var item in request.Items
                     .OrderBy(i => i.EntitlementId.Value, StringComparer.Ordinal)
                     .ThenBy(i => i.Provider.ToString(), StringComparer.Ordinal))
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

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
