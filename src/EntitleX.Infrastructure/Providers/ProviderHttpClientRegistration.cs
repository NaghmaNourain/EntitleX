using EntitleX.BuildingBlocks.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace EntitleX.Infrastructure.Providers;

internal static class ProviderHttpClientRegistration
{
    public static IServiceCollection AddProviderHttpClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ProviderOptions>(configuration.GetSection(ProviderOptions.SectionName));
        services.AddTransient<CorrelationIdDelegatingHandler>();

        var options = configuration.GetSection(ProviderOptions.SectionName).Get<ProviderOptions>()
            ?? new ProviderOptions();

        AddIdempotentGetClient(
            services,
            ProviderHttpClientNames.CloudShield,
            options.CloudShield,
            "cloudshield-get",
            "CloudShield");

        AddIdempotentGetClient(
            services,
            ProviderHttpClientNames.NetworkGuard,
            options.NetworkGuard,
            "networkguard-get",
            "NetworkGuard");

        // Command clients intentionally omit retry — POSTs are not blindly retried.
        AddCommandClient(
            services,
            ProviderHttpClientNames.CloudShieldCommands,
            options.CloudShield,
            "cloudshield-commands",
            "CloudShield");

        AddCommandClient(
            services,
            ProviderHttpClientNames.NetworkGuardCommands,
            options.NetworkGuard,
            "networkguard-commands",
            "NetworkGuard");

        return services;
    }

    private static void AddIdempotentGetClient(
        IServiceCollection services,
        string name,
        ProviderEndpointOptions endpoint,
        string pipelineName,
        string providerLabel)
    {
        services.AddHttpClient(name, client =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(endpoint.BaseUrl));
                client.Timeout = Timeout.InfiniteTimeSpan; // Polly owns timeouts
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler(() => new ProviderLatencyDelegatingHandler(providerLabel))
            .AddResilienceHandler(pipelineName, builder =>
            {
                builder.AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = endpoint.Timeout
                });

                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = endpoint.RetryCount,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args =>
                    {
                        // Defense in depth: only retry idempotent GETs on this client.
                        var method = args.Context.GetRequestMessage()?.Method;
                        if (method != HttpMethod.Get)
                        {
                            return PredicateResult.False();
                        }

                        return HttpClientResiliencePredicates.IsTransient(args.Outcome)
                            ? PredicateResult.True()
                            : PredicateResult.False();
                    }
                });

                builder.AddCircuitBreaker(CreateCircuitBreakerOptions(endpoint, providerLabel));
                builder.AddConcurrencyLimiter(
                    endpoint.BulkheadMaxParallelization,
                    endpoint.BulkheadQueueLimit);
            });
    }

    private static void AddCommandClient(
        IServiceCollection services,
        string name,
        ProviderEndpointOptions endpoint,
        string pipelineName,
        string providerLabel)
    {
        services.AddHttpClient(name, client =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(endpoint.BaseUrl));
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddHttpMessageHandler(() => new ProviderLatencyDelegatingHandler(providerLabel))
            .AddResilienceHandler(pipelineName, builder =>
            {
                builder.AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = endpoint.Timeout
                });

                // No retry strategy — non-idempotent POSTs must not be blindly retried.
                builder.AddCircuitBreaker(CreateCircuitBreakerOptions(endpoint, providerLabel));
                builder.AddConcurrencyLimiter(
                    endpoint.BulkheadMaxParallelization,
                    endpoint.BulkheadQueueLimit);
            });
    }

    private static HttpCircuitBreakerStrategyOptions CreateCircuitBreakerOptions(
        ProviderEndpointOptions endpoint,
        string providerLabel) =>
        new()
        {
            SamplingDuration = endpoint.CircuitBreakerSamplingDuration,
            FailureRatio = endpoint.CircuitBreakerFailureRatio,
            MinimumThroughput = endpoint.CircuitBreakerMinimumThroughput,
            BreakDuration = endpoint.CircuitBreakerBreakDuration,
            ShouldHandle = args =>
                HttpClientResiliencePredicates.IsTransient(args.Outcome)
                    ? PredicateResult.True()
                    : PredicateResult.False(),
            OnOpened = _ =>
            {
                RecordCircuitTransition(providerLabel, "opened");
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                RecordCircuitTransition(providerLabel, "closed");
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = _ =>
            {
                RecordCircuitTransition(providerLabel, "half_opened");
                return ValueTask.CompletedTask;
            }
        };

    private static void RecordCircuitTransition(string provider, string state) =>
        EntitleXMeters.CircuitBreakerTransitions.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("state", state));

    private static string EnsureTrailingSlash(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Provider BaseUrl is not configured.");
        }

        return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }
}
