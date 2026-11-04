using EntitleX.Domain.Identity;
using EntitleX.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EntitleX.Api.Endpoints;

/// <summary>Development-only JWT minting for local RBAC testing.</summary>
public static class DevTokenEndpoints
{
    public static IEndpointRouteBuilder MapDevTokenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/dev/tokens", MintAsync)
            .WithName("MintDevToken")
            .WithTags("Development")
            .WithOpenApi()
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult MintAsync(
        [FromBody] DevTokenRequest request,
        IJwtTokenService tokens,
        IOptions<JwtOptions> jwtOptions,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || !jwtOptions.Value.EnableDevTokenEndpoint)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Role)
            || !EntitleXRoles.All.Contains(request.Role, StringComparer.Ordinal))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] =
                [
                    $"Role must be one of: {string.Join(", ", EntitleXRoles.All)}"
                ]
            });
        }

        var seed = DevUserDirectory.Resolve(request.Role);
        var token = tokens.CreateToken(
            seed.UserId,
            seed.DisplayName,
            seed.Email,
            [request.Role]);

        return Results.Ok(new
        {
            accessToken = token,
            tokenType = "Bearer",
            expiresInMinutes = jwtOptions.Value.AccessTokenLifetimeMinutes,
            user = new
            {
                seed.UserId,
                seed.DisplayName,
                seed.Email,
                role = request.Role
            },
            usage = "Authorization: Bearer <accessToken>"
        });
    }
}

public sealed record DevTokenRequest(string Role);

internal static class DevUserDirectory
{
    public static (string UserId, string DisplayName, string Email) Resolve(string role) =>
        role switch
        {
            EntitleXRoles.RenewalRepresentative =>
                ("user-rep-001", "Riley Rep", "renewal.rep@entitlex.local"),
            EntitleXRoles.RenewalManager =>
                ("user-rm-001", "Morgan Manager", "renewal.manager@entitlex.local"),
            EntitleXRoles.DealDeskManager =>
                ("user-dd-001", "Dana DealDesk", "dealdesk.manager@entitlex.local"),
            EntitleXRoles.PlatformAdmin =>
                ("user-admin-001", "Alex Admin", "platform.admin@entitlex.local"),
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
}
