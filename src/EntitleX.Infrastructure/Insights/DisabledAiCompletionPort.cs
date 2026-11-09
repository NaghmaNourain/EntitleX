using EntitleX.Application.Insights.Abstractions;

namespace EntitleX.Infrastructure.Insights;

/// <summary>No-op port used when AiAssist is not registered (tests / hosts without HTTP).</summary>
public sealed class DisabledAiCompletionPort : IAiCompletionPort
{
    public bool IsEnabled => false;

    public string? Model => null;

    public Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
