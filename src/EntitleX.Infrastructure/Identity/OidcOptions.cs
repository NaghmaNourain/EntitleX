namespace EntitleX.Infrastructure.Identity;

/// <summary>
/// Generic OIDC settings for Keycloak (local) or Entra/Auth0/Cognito (cloud).
/// When <see cref="Enabled"/> is true, JWT Bearer validates against the IdP metadata.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>When true, JwtBearer uses Authority metadata instead of (or in addition to) HMAC.</summary>
    public bool Enabled { get; set; }

    /// <summary>IdP issuer / authority URL (e.g. http://localhost:8080/realms/entitlex).</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Expected access-token audience (API resource identifier).</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>SPA / confidential client id (documentation + optional azp checks).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Claim type carrying role names (flat multi-value). Keycloak realm import maps to <c>roles</c>.
    /// Use <c>realm_access.roles</c> to parse Keycloak's nested JSON when no protocol mapper is present.
    /// </summary>
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>
    /// Optional IdP role name → EntitleX role name map.
    /// Unmapped values that already match <see cref="Domain.Identity.EntitleXRoles"/> pass through.
    /// </summary>
    public Dictionary<string, string> RoleMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
