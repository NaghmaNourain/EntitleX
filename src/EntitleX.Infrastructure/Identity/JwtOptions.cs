namespace EntitleX.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC signing key (Development). Must be ≥ 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "EntitleX";

    public string Audience { get; set; } = "EntitleX";

    /// <summary>Access-token lifetime for minting (dev tokens).</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 480;

    /// <summary>When true, exposes Development-only token minting endpoint.</summary>
    public bool EnableDevTokenEndpoint { get; set; }
}
