namespace EntitleX.BuildingBlocks.Security;

/// <summary>Optional Basic credentials for Prometheus scrape endpoints outside Development.</summary>
public sealed class MetricsBasicAuthOptions
{
    public const string SectionName = "Metrics";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
