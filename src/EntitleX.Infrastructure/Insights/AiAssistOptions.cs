namespace EntitleX.Infrastructure.Insights;

/// <summary>
/// Optional local AI assist (Ollama via Microsoft.Extensions.AI / OllamaSharp).
/// Disabled unless Enabled=true. Never required for AWS lab — Fallback insights always work.
/// </summary>
public sealed class AiAssistOptions
{
    public const string SectionName = "AiAssist";

    /// <summary>When false (default), insights use deterministic Fallback only (no IChatClient).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Ollama base URL. Native Ollama is <c>http://127.0.0.1:11434</c>.
    /// A legacy OpenAI-compatible suffix <c>/v1</c> is stripped when constructing OllamaSharp.
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    /// <summary>Ignored by Ollama; optional override via ENTITLEX_AI_API_KEY.</summary>
    public string ApiKey { get; set; } = "ollama";

    public string Model { get; set; } = "llama3.2";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Chat temperature (default 0 for deterministic structured diagnosis).</summary>
    public float Temperature { get; set; }
}
