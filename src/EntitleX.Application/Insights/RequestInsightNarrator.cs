using EntitleX.Application.Insights.Abstractions;
using EntitleX.Application.Insights.Models;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Insights;

/// <summary>
/// Applies optional LLM rephrasing to the deterministic summary only.
/// RecommendedAction, citations, and facts always stay composer-authored.
/// </summary>
public sealed class RequestInsightNarrator
{
    private readonly IAiCompletionPort _completion;
    private readonly ILogger<RequestInsightNarrator> _logger;

    public RequestInsightNarrator(
        IAiCompletionPort completion,
        ILogger<RequestInsightNarrator> logger)
    {
        _completion = completion;
        _logger = logger;
    }

    public async Task<RequestInsightDto> NarrateAsync(
        Guid requestId,
        string insightKind,
        DeterministicInsightComposer.ComposedInsight composed,
        CancellationToken cancellationToken)
    {
        var summary = composed.Summary;
        var mode = "Fallback";
        string? model = null;

        if (_completion.IsEnabled)
        {
            var system = insightKind.Equals("Rca", StringComparison.OrdinalIgnoreCase)
                ? SystemPromptRca
                : SystemPromptExplain;

            var user =
                $"RecommendedAction enum (must keep this intent): {composed.RecommendedAction}\n" +
                $"Deterministic summary (may rephrase, not contradict):\n{composed.Summary}\n\n" +
                $"Facts JSON:\n{composed.FactsJson}";

            try
            {
                var rewritten = await _completion.CompleteAsync(system, user, cancellationToken);
                if (!string.IsNullOrWhiteSpace(rewritten))
                {
                    summary = rewritten.Trim();
                    mode = "Llm";
                    model = _completion.Model;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Insight LLM narration failed; using Fallback summary");
            }
        }

        return new RequestInsightDto(
            requestId,
            insightKind,
            summary,
            composed.RecommendedAction.ToString(),
            mode,
            model,
            composed.Citations,
            composed.Facts);
    }

    private const string SystemPromptExplain =
        "You are EntitleX control-plane status narrator. Rephrase the deterministic summary using ONLY the provided Facts JSON. "
        + "Never invent IDs, statuses, or claim actions were taken. Max 12 sentences. "
        + "End by restating the same RecommendedAction enum intent. Do not suggest auto-mutating fulfillment.";

    private const string SystemPromptRca =
        "You are EntitleX reconciliation RCA narrator. Emphasize Uncertain items, ledger mismatches, provider probes, and poison outbox. "
        + "Use ONLY the provided Facts JSON. Never invent IDs or claim repairs ran. Max 12 sentences. "
        + "End by restating the same RecommendedAction enum intent. Do not auto-reconcile or retry.";
}
