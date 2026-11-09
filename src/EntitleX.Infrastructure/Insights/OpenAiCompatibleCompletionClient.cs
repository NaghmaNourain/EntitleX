using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntitleX.Application.Insights.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Insights;

public sealed class OpenAiCompatibleCompletionClient : IAiCompletionPort
{
    public const string HttpClientName = "EntitleX.AiAssist";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiAssistOptions _options;
    private readonly ILogger<OpenAiCompatibleCompletionClient> _logger;

    public OpenAiCompatibleCompletionClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AiAssistOptions> options,
        ILogger<OpenAiCompatibleCompletionClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.BaseUrl)
        && !string.IsNullOrWhiteSpace(_options.Model);

    public string? Model => IsEnabled ? _options.Model : null;

    public async Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Content = JsonContent.Create(new ChatCompletionRequest(
                _options.Model,
                [
                    new ChatMessage("system", systemPrompt),
                    new ChatMessage("user", userPrompt)
                ],
                Temperature: 0.2));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "AiAssist chat completion failed with {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    Truncate(body));
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(
                stream,
                cancellationToken: cancellationToken);
            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AiAssist chat completion threw; using Fallback narration");
            return null;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500];

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessageDto? Message { get; set; }
    }

    private sealed class ChatMessageDto
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
