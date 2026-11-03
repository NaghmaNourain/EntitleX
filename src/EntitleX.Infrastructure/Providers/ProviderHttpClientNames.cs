namespace EntitleX.Infrastructure.Providers;

public static class ProviderHttpClientNames
{
    public const string CloudShield = "CloudShield";
    public const string NetworkGuard = "NetworkGuard";

    /// <summary>
    /// Named client intended for non-idempotent POSTs (extensions). Configured
    /// with timeout + circuit breaker + bulkhead, but <b>without</b> automatic retries.
    /// </summary>
    public const string CloudShieldCommands = "CloudShieldCommands";

    public const string NetworkGuardCommands = "NetworkGuardCommands";
}
