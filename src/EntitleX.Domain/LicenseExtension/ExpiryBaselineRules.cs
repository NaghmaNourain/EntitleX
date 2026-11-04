using EntitleX.Domain.Common;

namespace EntitleX.Domain.LicenseExtension;

/// <summary>
/// Expiry baseline rule stub: requested expiry must be strictly after the
/// entitlement's current expiry (the baseline captured at request creation).
/// </summary>
public static class ExpiryBaselineRules
{
    public static void EnsureRequestedExpiryAfterBaseline(
        DateTimeOffset currentExpiryUtc,
        DateTimeOffset requestedExpiryUtc)
    {
        if (requestedExpiryUtc <= currentExpiryUtc)
        {
            throw new DomainException(
                "Requested expiry must be strictly after the current expiry baseline.");
        }
    }
}
