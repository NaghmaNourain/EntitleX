using System.Runtime.CompilerServices;
using System.Text.Json;
using EntitleX.Application.Insights;
using EntitleX.Application.Insights.Abstractions;
using EntitleX.Application.Insights.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntitleX.Application.UnitTests.Insights;

public sealed class RequestInsightNarratorTests
{
    private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly string EvidenceId = RequestId.ToString("D");

    [Fact]
    public async Task Disabled_Ai_Returns_Fallback_Without_Diagnosis()
    {
        var composed = Compose();
        var narrator = CreateNarrator(
            enabled: false,
            chatClient: new ScriptedChatClient(_ => throw new InvalidOperationException("should not call")));

        var dto = await narrator.NarrateAsync(RequestId, "Explain", composed, CancellationToken.None);

        Assert.Equal("Fallback", dto.NarrationMode);
        Assert.Null(dto.Diagnosis);
        Assert.Equal(composed.Summary, dto.Summary);
        Assert.Equal(composed.RecommendedAction.ToString(), dto.RecommendedAction);
    }

    [Fact]
    public async Task Valid_Structured_Output_Returns_Llm_Diagnosis()
    {
        var composed = Compose();
        var valid = new IncidentDiagnosis(
            RootCauseCategories.ProviderTimeout,
            "Provider timed out while applying the extension.",
            [EvidenceId],
            0.91,
            "HoldUncertain",
            RequiresHumanApproval: true);

        var client = new ScriptedChatClient(_ => Task.FromResult(JsonResponse(valid)));
        var narrator = CreateNarrator(enabled: true, chatClient: client, model: "qwen2.5:7b");

        var dto = await narrator.NarrateAsync(RequestId, "Explain", composed, CancellationToken.None);

        Assert.Equal("Llm", dto.NarrationMode);
        Assert.Equal("qwen2.5:7b", dto.Model);
        Assert.Equal(valid.RootCause, dto.Summary);
        Assert.Equal(composed.RecommendedAction.ToString(), dto.RecommendedAction);
        Assert.NotNull(dto.Diagnosis);
        Assert.True(dto.Diagnosis!.Validated);
        Assert.Equal(RootCauseCategories.ProviderTimeout, dto.Diagnosis.RootCauseCategory);
        Assert.Equal(0.91, dto.Diagnosis.Confidence);
        Assert.Equal([EvidenceId], dto.Diagnosis.EvidenceStepIds.ToArray());
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Hallucinated_EvidenceStepId_Repairs_Then_Falls_Back()
    {
        var composed = Compose();
        var hallucinated = new IncidentDiagnosis(
            RootCauseCategories.ProviderRejected,
            "Provider rejected the operation.",
            ["00000000-0000-0000-0000-000000000099"],
            0.8,
            "ManualReconcile",
            RequiresHumanApproval: true);

        var client = new ScriptedChatClient(_ => Task.FromResult(JsonResponse(hallucinated)));
        var narrator = CreateNarrator(enabled: true, chatClient: client);

        var dto = await narrator.NarrateAsync(RequestId, "Explain", composed, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        Assert.Equal("Fallback", dto.NarrationMode);
        Assert.Equal(composed.Summary, dto.Summary);
        Assert.NotNull(dto.Diagnosis);
        Assert.False(dto.Diagnosis!.Validated);
        Assert.Equal("Unable to diagnose", dto.Diagnosis.RootCause);
        Assert.Equal(RootCauseCategories.Unknown, dto.Diagnosis.RootCauseCategory);
    }

    [Fact]
    public async Task Malformed_Json_Falls_Back_After_Repair()
    {
        var composed = Compose();
        var client = new ScriptedChatClient(_ =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "not-json-at-all"))));
        var narrator = CreateNarrator(enabled: true, chatClient: client);

        var dto = await narrator.NarrateAsync(RequestId, "Rca", composed, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        Assert.Equal("Fallback", dto.NarrationMode);
        Assert.NotNull(dto.Diagnosis);
        Assert.False(dto.Diagnosis!.Validated);
        Assert.Equal("Unable to diagnose", dto.Diagnosis.RootCause);
    }

    [Fact]
    public async Task Hallucinated_Evidence_Can_Be_Repaired_On_Second_Call()
    {
        var composed = Compose();
        var bad = new IncidentDiagnosis(
            RootCauseCategories.PoisonOutbox,
            "Poisoned outbox blocked fulfillment.",
            ["not-a-real-id"],
            0.7,
            "InvestigatePoisonOutbox",
            RequiresHumanApproval: true);
        var good = new IncidentDiagnosis(
            RootCauseCategories.PoisonOutbox,
            "Poisoned outbox blocked fulfillment.",
            [EvidenceId],
            0.7,
            "InvestigatePoisonOutbox",
            RequiresHumanApproval: true);

        var call = 0;
        var client = new ScriptedChatClient(_ =>
        {
            call++;
            return Task.FromResult(JsonResponse(call == 1 ? bad : good));
        });
        var narrator = CreateNarrator(enabled: true, chatClient: client);

        var dto = await narrator.NarrateAsync(RequestId, "Explain", composed, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
        Assert.Equal("Llm", dto.NarrationMode);
        Assert.NotNull(dto.Diagnosis);
        Assert.True(dto.Diagnosis!.Validated);
        Assert.Equal([EvidenceId], dto.Diagnosis.EvidenceStepIds.ToArray());
    }

    private static DeterministicInsightComposer.ComposedInsight Compose()
    {
        var context = new RequestInsightContext
        {
            RequestId = RequestId,
            Request = null,
            Approval = null,
            Saga = null,
            Items = [],
            PoisonOutbox = [],
            RecentAudit = [],
            BuilderNotes = ["unit-test"]
        };
        return new DeterministicInsightComposer().Compose(context, "Explain");
    }

    private static RequestInsightNarrator CreateNarrator(
        bool enabled,
        IChatClient chatClient,
        string model = "test-model") =>
        new(
            new FakeGate(enabled, model),
            enabled ? [chatClient] : Array.Empty<IChatClient>(),
            new IncidentDiagnosisValidator(),
            NullLogger<RequestInsightNarrator>.Instance);

    private static ChatResponse JsonResponse(IncidentDiagnosis diagnosis)
    {
        var json = JsonSerializer.Serialize(diagnosis, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
    }

    private sealed class FakeGate(bool enabled, string model) : IAiAssistGate
    {
        public bool IsEnabled => enabled;
        public string? Model => enabled ? model : null;
        public float Temperature => 0;
    }

    private sealed class ScriptedChatClient(
        Func<IEnumerable<ChatMessage>, Task<ChatResponse>> handler) : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return handler(messages);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
