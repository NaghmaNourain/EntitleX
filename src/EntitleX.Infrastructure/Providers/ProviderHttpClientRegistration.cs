using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
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
            "cloudshield-get");

        AddIdempotentGetClient(
            services,
            ProviderHttpClientNames.NetworkGuard,
            options.NetworkGuard,
            "networkguard-get");

        // Command clients intentionally omit retry — POSTs are not blindly retried.
        AddCommandClient(
            services,
            ProviderHttpClientNames.CloudShieldCommands,
            options.CloudShield,
            "cloudshield-commands");

        AddCommandClient(
            services,
            ProviderHttpClientNames.NetworkGuardCommands,
            options.NetworkGuard,
            "networkguard-commands");

        return services;
    }

    private static void AddIdempotentGetClient(
        IServiceCollection services,
        string name,
        ProviderEndpointOptions endpoint,
        string pipelineName)
    {
        services.AddHttpClient(name, client =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(endpoint.BaseUrl));
                client.Timeout = Timeout.InfiniteTimeSpan; // Polly owns timeouts
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
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

                builder.AddCircuitBreaker(CreateCircuitBreakerOptions(endpoint));
                builder.AddConcurrencyLimiter(
                    endpoint.BulkheadMaxParallelization,
                    endpoint.BulkheadQueueLimit);
            });
    }

    private static void AddCommandClient(
        IServiceCollection services,
        string name,
        ProviderEndpointOptions endpoint,
        string pipelineName)
    {
        services.AddHttpClient(name, client =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(endpoint.BaseUrl));
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddResilienceHandler(pipelineName, builder =>
            {
                builder.AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = endpoint.Timeout
                });

                // No retry strategy — non-idempotent POSTs must not be blindly retried.
                builder.AddCircuitBreaker(CreateCircuitBreakerOptions(endpoint));
                builder.AddConcurrencyLimiter(
                    endpoint.BulkheadMaxParallelization,
                    endpoint.BulkheadQueueLimit);
            });
    }

    private static HttpCircuitBreakerStrategyOptions CreateCircuitBreakerOptions(
        ProviderEndpointOptions endpoint) =>
        new()
        {
            SamplingDuration = endpoint.CircuitBreakerSamplingDuration,
            FailureRatio = endpoint.CircuitBreakerFailureRatio,
            MinimumThroughput = endpoint.CircuitBreakerMinimumThroughput,
            BreakDuration = endpoint.CircuitBreakerBreakDuration,
            ShouldHandle = args =>
                HttpClientResiliencePredicates.IsTransient(args.Outcome)
                    ? PredicateResult.True()
                    : PredicateResult.False()
        };

    private static string EnsureTrailingSlash(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Provider BaseUrl is not configured.");
        }

        return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }
}
