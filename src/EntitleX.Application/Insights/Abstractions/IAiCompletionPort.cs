namespace EntitleX.Application.Insights.Abstractions;

/// <summary>
/// Optional OpenAI-compatible chat completion (local Ollama by default).
/// Returns null when disabled, unreachable, or on error — callers keep Fallback narration.
/// </summary>
public interface IAiCompletionPort
{
    bool IsEnabled { get; }

    string? Model { get; }

    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
