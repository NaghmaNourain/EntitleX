namespace EntitleX.Infrastructure.Insights;

public static class AiAssistEndpointNormalizer
{
    /// <summary>
    /// OllamaSharp talks to the native Ollama HTTP API (no /v1). Strip a trailing /v1
    /// so existing OpenAI-compatible BaseUrl values keep working.
    /// </summary>
    public static Uri ToOllamaUri(string? baseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://127.0.0.1:11434"
            : baseUrl.Trim().TrimEnd('/');

        if (raw.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^3].TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "http://127.0.0.1:11434";
        }

        return new Uri(raw + "/");
    }
}
