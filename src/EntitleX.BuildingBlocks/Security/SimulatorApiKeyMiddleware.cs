using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EntitleX.BuildingBlocks.Security;

/// <summary>
/// Requires a configured API key on simulator endpoints (health/metrics exempt).
/// Rejects with 401 when the key is missing/mismatched; refuses startup-time empty keys
/// by returning 503 so misconfigured labs fail closed rather than open.
/// </summary>
public sealed class SimulatorApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public SimulatorApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<SimulatorApiKeyOptions> optionsAccessor)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics"))
        {
            await _next(context);
            return;
        }

        var options = optionsAccessor.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Simulator auth misconfigured",
                detail = "SimulatorAuth:ApiKey must be configured."
            });
            return;
        }

        if (!TryGetPresentedKey(context.Request, options.HeaderName, out var presented)
            || !FixedTimeEquals(presented, options.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "ApiKey";
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Unauthorized",
                detail = "Valid simulator API key required."
            });
            return;
        }

        await _next(context);
    }

    private static bool TryGetPresentedKey(HttpRequest request, string headerName, out string key)
    {
        if (request.Headers.TryGetValue(headerName, out var headerValues))
        {
            var raw = headerValues.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                key = raw.Trim();
                return true;
            }
        }

        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
        {
            key = auth["ApiKey ".Length..].Trim();
            return !string.IsNullOrWhiteSpace(key);
        }

        key = string.Empty;
        return false;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length)
        {
            // Still touch both buffers so length mismatches are not a pure short-circuit oracle.
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

public static class SimulatorApiKeyExtensions
{
    public static IServiceCollection AddSimulatorApiKeyAuth(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<SimulatorApiKeyOptions>(
            configuration.GetSection(SimulatorApiKeyOptions.SectionName));
        return services;
    }

    public static IApplicationBuilder UseSimulatorApiKeyAuth(this IApplicationBuilder app) =>
        app.UseMiddleware<SimulatorApiKeyMiddleware>();
}
