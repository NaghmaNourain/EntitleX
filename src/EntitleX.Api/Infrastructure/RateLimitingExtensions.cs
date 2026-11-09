using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace EntitleX.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Fixed-window permits for anonymous clients (per remote IP).</summary>
    public int AnonymousPermitLimit { get; set; } = 60;

    /// <summary>Fixed-window permits for authenticated clients (per user id).</summary>
    public int AuthenticatedPermitLimit { get; set; } = 120;

    /// <summary>Window length for anonymous + authenticated global limits.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Sliding-window permits for write endpoints (create/decide/retry/ops).</summary>
    public int WritePermitLimit { get; set; } = 30;

    public int WriteWindowSeconds { get; set; } = 60;

    public int QueueLimit { get; set; } = 0;
}

public static class RateLimitingExtensions
{
    public const string WritePolicy = "write";

    public static IServiceCollection AddEntitleXRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
                      ?? new RateLimitingOptions();

        // Always register so .RequireRateLimiting("write") is valid; when disabled use a generous limit.
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://httpstatuses.com/429",
                        title = "Too many requests",
                        status = 429,
                        detail = "Rate limit exceeded. Retry after the window resets.",
                        instance = context.HttpContext.Request.Path.Value
                    },
                    cancellationToken: token);
            };

            // Global: fixed-window per IP (anonymous) or per user (authenticated).
            // Covers /health*, DevToken minting, and all authenticated reads.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var authenticated = httpContext.User?.Identity?.IsAuthenticated == true;
                string partitionKey;
                int permitLimit;

                if (authenticated)
                {
                    var user = httpContext.User;
                    partitionKey = "user:" + (
                        user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? user.FindFirst("sub")?.Value
                        ?? "authenticated");
                    permitLimit = options.Enabled
                        ? Math.Max(1, options.AuthenticatedPermitLimit)
                        : 1_000_000;
                }
                else
                {
                    partitionKey = "ip:" + (
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous");
                    permitLimit = options.Enabled
                        ? Math.Max(1, options.AnonymousPermitLimit)
                        : 1_000_000;
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)),
                        QueueLimit = Math.Max(0, options.QueueLimit),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            limiter.AddPolicy(WritePolicy, httpContext =>
            {
                var partitionKey = httpContext.User?.Identity?.IsAuthenticated == true
                    ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? httpContext.User.FindFirst("sub")?.Value
                      ?? "authenticated"
                    : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                var permitLimit = options.Enabled
                    ? Math.Max(1, options.WritePermitLimit)
                    : 1_000_000;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(Math.Max(1, options.WriteWindowSeconds)),
                        SegmentsPerWindow = 6,
                        QueueLimit = Math.Max(0, options.QueueLimit),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        });

        return services;
    }

    public static IApplicationBuilder UseEntitleXRateLimiting(this IApplicationBuilder app)
    {
        app.UseRateLimiter();
        return app;
    }
}
