using System.Security.Claims;
using System.Text.Json;

namespace EntitleX.Infrastructure.Identity;

/// <summary>
/// Normalizes IdP <c>customer_ids</c> claims onto flat multi-value claims for <see cref="HttpCurrentUser"/>.
/// </summary>
public static class OidcCustomerClaimMapper
{
    public static void ApplyCustomerClaims(ClaimsIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var existing = identity.FindAll(HttpCurrentUser.CustomerIdsClaimType)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in identity.FindAll(HttpCurrentUser.CustomerIdsClaimType).ToArray())
        {
            if (LooksLikeJsonArray(claim.Value))
            {
                identity.RemoveClaim(claim);
                foreach (var id in ParseJsonStringArray(claim.Value))
                {
                    if (existing.Add(id))
                    {
                        identity.AddClaim(new Claim(HttpCurrentUser.CustomerIdsClaimType, id));
                    }
                }
            }
        }

        // Also accept comma-separated single claim values.
        foreach (var claim in identity.FindAll(HttpCurrentUser.CustomerIdsClaimType).ToArray())
        {
            if (claim.Value.Contains(',', StringComparison.Ordinal))
            {
                identity.RemoveClaim(claim);
                existing.Remove(claim.Value);
                foreach (var part in claim.Value.Split(
                             ',',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (existing.Add(part))
                    {
                        identity.AddClaim(new Claim(HttpCurrentUser.CustomerIdsClaimType, part));
                    }
                }
            }
        }
    }

    private static bool LooksLikeJsonArray(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.TrimStart().StartsWith('[');

    private static IEnumerable<string> ParseJsonStringArray(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var s = element.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        yield return s.Trim();
                    }
                }
            }
        }
    }
}
