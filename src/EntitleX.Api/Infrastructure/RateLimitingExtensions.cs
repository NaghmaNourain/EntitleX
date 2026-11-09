using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EntitleX.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

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
                        detail = "Write rate limit exceeded. Retry after the window resets.",
                        instance = context.HttpContext.Request.Path.Value
                    },
                    cancellationToken: token);
            };

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
