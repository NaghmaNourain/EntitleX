namespace EntitleX.Infrastructure.Insights;

/// <summary>
/// Optional OpenAI-compatible narration (local Ollama by default). Disabled unless Enabled=true.
/// Never required for AWS lab — Fallback narration always works without this.
/// </summary>
public sealed class AiAssistOptions
{
    public const string SectionName = "AiAssist";

    /// <summary>When false (default), insights use deterministic Fallback only.</summary>
    public bool Enabled { get; set; }

    /// <summary>OpenAI-compatible base URL including /v1 (Ollama: http://127.0.0.1:11434/v1).</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434/v1";

    /// <summary>Ignored by Ollama; optional override via ENTITLEX_AI_API_KEY.</summary>
    public string ApiKey { get; set; } = "ollama";

    public string Model { get; set; } = "llama3.2";

    public int TimeoutSeconds { get; set; } = 30;
}
