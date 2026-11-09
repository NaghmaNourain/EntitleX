namespace EntitleX.BuildingBlocks.Security;

/// <summary>Shared API-key gate for vendor simulator HTTP surfaces.</summary>
public sealed class SimulatorApiKeyOptions
{
    public const string SectionName = "SimulatorAuth";

    /// <summary>Expected value of the <c>X-Api-Key</c> (or Authorization: ApiKey) header.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Header name carrying the API key (default <c>X-Api-Key</c>).</summary>
    public string HeaderName { get; set; } = "X-Api-Key";
}
