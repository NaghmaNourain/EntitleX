using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EntitleX.Api.Infrastructure;

/// <summary>
/// Emits only aggregate status plus dependency names/statuses — no descriptions,
/// exception text, or timing data.
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString()
                })
                .ToArray()
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
