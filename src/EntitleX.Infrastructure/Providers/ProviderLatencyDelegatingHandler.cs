using System.Diagnostics;
using EntitleX.BuildingBlocks.Observability;

namespace EntitleX.Infrastructure.Providers;

/// <summary>
/// Records outbound provider HTTP latency. Labels: provider + operation kind only
/// (no URLs or IDs — path templates stay low-cardinality via client name).
/// </summary>
public sealed class ProviderLatencyDelegatingHandler : DelegatingHandler
{
    private readonly string _provider;

    public ProviderLatencyDelegatingHandler(string provider)
    {
        _provider = provider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var activity = EntitleXActivitySources.Providers.StartActivity(
            $"provider.{_provider}",
            ActivityKind.Client);
        activity?.SetTag("provider.name", _provider);
        activity?.SetTag("http.method", request.Method.Method);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            activity?.SetTag("http.status_code", (int)response.StatusCode);
            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            EntitleXMeters.ProviderLatencySeconds.Record(
                sw.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("provider", _provider),
                new KeyValuePair<string, object?>("method", request.Method.Method));
        }
    }
}
