namespace EntitleX.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC signing key for Development DevToken minting. Must be ≥ 32 characters when used.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "EntitleX";

    public string Audience { get; set; } = "EntitleX";

    /// <summary>Access-token lifetime for minting (dev tokens).</summary>
    /// <summary>DevToken / HMAC lifetime. Keep short in Development (default 60); Production uses OIDC.</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// When true, exposes Development-only token minting endpoint.
    /// Must remain false in Production (enforced at startup).
    /// </summary>
    public bool EnableDevTokenEndpoint { get; set; }

    /// <summary>
    /// When true, DevToken may mint <c>PlatformAdmin</c>. Defaults to false so a
    /// mis-set Development environment cannot anonymously mint full admin without intent.
    /// </summary>
    public bool AllowDevPlatformAdmin { get; set; }
}
