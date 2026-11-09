using System.Security.Claims;
using System.Text.Json;
using EntitleX.Domain.Identity;

namespace EntitleX.Infrastructure.Identity;

/// <summary>
/// Maps IdP role claims onto EntitleX RBAC role names (<see cref="EntitleXRoles"/>).
/// </summary>
public static class OidcRoleClaimMapper
{
    public static IReadOnlyList<string> ExtractMappedRoles(
        IEnumerable<Claim> claims,
        OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(options);

        var raw = ExtractRawRoleValues(claims, options.RoleClaimType);
        return MapToEntitleXRoles(raw, options.RoleMap);
    }

    /// <summary>
    /// Adds <see cref="ClaimTypes.Role"/> claims for each mapped EntitleX role (idempotent).
    /// </summary>
    public static void ApplyRoleClaims(ClaimsIdentity identity, OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        var mapped = ExtractMappedRoles(identity.Claims, options);
        var existing = identity.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var role in mapped)
        {
            if (existing.Add(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    }

    public static IReadOnlyList<string> MapToEntitleXRoles(
        IEnumerable<string> rawRoles,
        IReadOnlyDictionary<string, string>? roleMap)
    {
        ArgumentNullException.ThrowIfNull(rawRoles);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in rawRoles)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var candidate = raw.Trim();
            if (roleMap is not null)
            {
                string? mapped = null;
                if (roleMap.TryGetValue(candidate, out var exact))
                {
                    mapped = exact;
                }
                else
                {
                    foreach (var pair in roleMap)
                    {
                        if (string.Equals(pair.Key, candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            mapped = pair.Value;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    candidate = mapped.Trim();
                }
            }

            if (EntitleXRoles.All.Contains(candidate, StringComparer.Ordinal))
            {
                result.Add(candidate);
            }
        }

        return result.OrderBy(r => r, StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> ExtractRawRoleValues(
        IEnumerable<Claim> claims,
        string roleClaimType)
    {
        var claimType = string.IsNullOrWhiteSpace(roleClaimType) ? "roles" : roleClaimType.Trim();
        var list = new List<string>();

        if (claimType.Equals("realm_access.roles", StringComparison.OrdinalIgnoreCase)
            || claimType.Equals("realm_access", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var claim in claims.Where(c =>
                         c.Type is "realm_access" or "realm_access.roles"))
            {
                list.AddRange(ParseRealmAccessRoles(claim.Value));
            }

            return list;
        }

        foreach (var claim in claims.Where(c =>
                     string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase)
                     || (claimType == "roles"
                         && (c.Type == ClaimTypes.Role
                             || c.Type.EndsWith("/roles", StringComparison.OrdinalIgnoreCase)))))
        {
            if (LooksLikeJsonArray(claim.Value))
            {
                list.AddRange(ParseJsonStringArray(claim.Value));
            }
            else
            {
                list.Add(claim.Value);
            }
        }

        return list;
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
                        yield return s;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ParseRealmAccessRoles(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        // Already a flat role name (unlikely for realm_access).
        if (!value.TrimStart().StartsWith('{')
            && !value.TrimStart().StartsWith('['))
        {
            yield return value;
            yield break;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in ParseJsonStringArray(value))
                {
                    yield return role;
                }

                yield break;
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in roles.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var s = element.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            yield return s;
                        }
                    }
                }
            }
        }
    }
}
