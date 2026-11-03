using EntitleX.Infrastructure.Observability;

namespace EntitleX.Infrastructure.Providers;

/// <summary>Propagates the ambient correlation id on outbound provider calls.</summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    public const string HeaderName = "X-Correlation-ID";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationContext.Current;
        if (!string.IsNullOrWhiteSpace(correlationId)
            && !request.Headers.Contains(HeaderName))
        {
            request.Headers.TryAddWithoutValidation(HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
