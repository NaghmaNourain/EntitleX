using System.Net.Http.Headers;
using System.Text;
using EntitleX.BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;

namespace EntitleX.Api.Infrastructure;

public static class MetricsAuthExtensions
{
    /// <summary>
    /// Outside Development, require authenticated scrape (Basic when Metrics credentials are set,
    /// otherwise the default JWT fallback policy). Development leaves /metrics anonymous for local Prometheus.
    /// </summary>
    public static IEndpointConventionBuilder RequireEntitleXMetricsAuth(
        this IEndpointConventionBuilder builder,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (environment.IsDevelopment())
        {
            return builder.AllowAnonymous();
        }

        var metrics = configuration.GetSection(MetricsBasicAuthOptions.SectionName)
            .Get<MetricsBasicAuthOptions>() ?? new MetricsBasicAuthOptions();

        if (metrics.IsConfigured)
        {
            return builder
                .AllowAnonymous()
                .AddEndpointFilter(new MetricsBasicAuthFilter(metrics.Username, metrics.Password));
        }

        return builder.RequireAuthorization();
    }

    private sealed class MetricsBasicAuthFilter(string username, string password) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext context,
            EndpointFilterDelegate next)
        {
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            if (AuthenticationHeaderValue.TryParse(header, out var parsed)
                && string.Equals(parsed.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parsed.Parameter))
            {
                string decoded;
                try
                {
                    decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
                }
                catch (FormatException)
                {
                    return Unauthorized(context);
                }

                var separator = decoded.IndexOf(':');
                if (separator > 0)
                {
                    var user = decoded[..separator];
                    var pass = decoded[(separator + 1)..];
                    if (FixedTimeEquals(user, username) && FixedTimeEquals(pass, password))
                    {
                        return await next(context);
                    }
                }
            }

            return Unauthorized(context);
        }

        private static IResult Unauthorized(EndpointFilterInvocationContext context)
        {
            context.HttpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"EntitleX metrics\"";
            return Results.Unauthorized();
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);
            if (aBytes.Length != bBytes.Length)
            {
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
                return false;
            }

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
