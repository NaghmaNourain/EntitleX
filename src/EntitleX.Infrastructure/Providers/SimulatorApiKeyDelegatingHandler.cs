namespace EntitleX.Infrastructure.Providers;

/// <summary>Attaches the configured simulator API key to outbound provider HTTP calls.</summary>
internal sealed class SimulatorApiKeyDelegatingHandler : DelegatingHandler
{
    private readonly string _apiKey;

    public SimulatorApiKeyDelegatingHandler(string apiKey)
    {
        _apiKey = apiKey;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey)
            && !request.Headers.Contains("X-Api-Key"))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
