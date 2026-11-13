# EntitleX AI agent (Insights)

Optional, local-first AI assist for Explain / reconciliation RCA. **Default is off** (`AiAssist:Enabled=false`). CI, AWS lab, and tests must behave exactly as without AI.

## Phase 1 (current): structured diagnosis

When enabled, Insights use **Microsoft.Extensions.AI** `IChatClient` (Ollama via **OllamaSharp**) instead of free-text rephrasing.

Pipeline (enabled only):

`OllamaApiClient` → `UseOpenTelemetry` → `UseLogging`

Flow:

1. Existing ACL-aware context builder + deterministic composer still produce facts, citations, and `RecommendedAction`.
2. `RequestInsightNarrator` calls `GetResponseAsync<IncidentDiagnosis>` with untrusted facts wrapped in clear delimiters.
3. Server-side validation: category allowlist, confidence ∈ [0,1], every `EvidenceStepId` must appear in composer citations.
4. One repair retry on validation failure; then a safe **Unable to diagnose** result. Unvalidated model output is never returned as `validated: true`.

### Response contract

Existing `RequestInsight` fields are unchanged. Optional:

```json
"diagnosis": {
  "rootCauseCategory": "ProviderTimeout",
  "rootCause": "...",
  "evidenceStepIds": ["..."],
  "confidence": 0.9,
  "recommendedAction": "HoldUncertain",
  "requiresHumanApproval": true,
  "validated": true
}
```

When AI is disabled, `diagnosis` is omitted/`null` and `narrationMode` is `Fallback`.

### RootCauseCategory allowlist

Derived from saga / reconciliation failure modes (see [failure-scenarios.md](failure-scenarios.md)):

`ProviderTimeout`, `ProviderStatusUnknown`, `ProviderNotFound`, `ProviderRejected`, `DuplicateApply`, `MessageRedelivery`, `ReconciliationMismatch`, `PoisonOutbox`, `ApprovalTimeout`, `ConcurrentRetryConflict`, `Unknown`.

### Config

| Key | Default | Notes |
|---|---|---|
| `AiAssist:Enabled` | `false` | Must stay false in CI / AWS lab |
| `AiAssist:BaseUrl` | `http://127.0.0.1:11434` | Trailing `/v1` is stripped for OllamaSharp |
| `AiAssist:Model` | `llama3.2` | e.g. `qwen2.5:7b` for demos |
| `AiAssist:Temperature` | `0` | Structured diagnosis |
| `AiAssist:TimeoutSeconds` | `30` | |
| `ENTITLEX_AI_API_KEY` | optional | Ollama ignores auth |

### Local demo

```bash
ollama pull qwen2.5:7b
# Api / env:
# AiAssist__Enabled=true
# AiAssist__BaseUrl=http://127.0.0.1:11434
# AiAssist__Model=qwen2.5:7b
# AiAssist__Temperature=0

dotnet run --project src/EntitleX.Api
# Open a failed request → Explain / RCA → structured diagnosis in UI
# Jaeger: GenAI spans from UseOpenTelemetry on the chat pipeline
```

### Tests

Unit tests under `tests/EntitleX.Application.UnitTests/Insights` cover:

- valid structured output
- hallucinated evidence → repair → fallback
- malformed JSON → fallback
- AI disabled → Fallback, `diagnosis` null

## Later phases (not implemented)

| Phase | Goal |
|---|---|
| 2 | Tool calling (`UseFunctionInvocation`), ACL-enforced tools, approval-gated write proposals |
| 3 | Deterministic eval harness (`EntitleX.AiEvals`) in CI (replay mode) |
| 4 | LLM-as-judge quality evals + HTML report |
| 5 | MCP server exposing **read** tools only via the API |

See also [reconciliation.md](reconciliation.md) (Insights section).
