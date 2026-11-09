using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace EntitleX.BuildingBlocks.Observability;

/// <summary>
/// Thin ASP.NET correlation middleware for hosts that do not use Api Infrastructure.
/// Pushes <c>CorrelationId</c> into Serilog LogContext from <c>X-Correlation-ID</c>.
/// </summary>
public sealed class CorrelationIdHttpMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdHttpMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var values)
            && !string.IsNullOrWhiteSpace(values.FirstOrDefault())
                ? values.First()!.Trim()
                : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;
        context.Items[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public static class CorrelationIdHttpMiddlewareExtensions
{
    public static IApplicationBuilder UseEntitleXCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdHttpMiddleware>();
}
