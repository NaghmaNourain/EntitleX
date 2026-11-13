using EntitleX.Infrastructure.Insights;

namespace EntitleX.Infrastructure.UnitTests.Insights;

public sealed class AiAssistEndpointNormalizerTests
{
    [Theory]
    [InlineData("http://127.0.0.1:11434", "http://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/", "http://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/")]
    [InlineData("http://127.0.0.1:11434/v1/", "http://127.0.0.1:11434/")]
    public void Strips_OpenAi_Compatible_V1_Suffix(string input, string expected)
    {
        var uri = AiAssistEndpointNormalizer.ToOllamaUri(input);
        Assert.Equal(expected, uri.ToString());
    }
}
