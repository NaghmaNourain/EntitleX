using EntitleX.Api.Infrastructure;
using EntitleX.Application.LicenseExtension.RetryFailed;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EntitleX.Api.Endpoints;

public static class LicenseExtensionRetryEndpoints
{
    public const string IdempotencyKeyHeader = IdempotencyKeyHelper.HeaderName;

    public static IEndpointRouteBuilder MapLicenseExtensionRetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/license-extensions")
            .WithTags("LicenseExtensions");

        group.MapPost("/{requestId:guid}/retry-failed", RetryFailedAsync)
            .WithName("RetryFailedLicenseExtensions")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanCreateRequests)
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> RetryFailedAsync(
        Guid requestId,
        [FromBody] RetryFailedRequestBody? body,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        HttpRequest httpRequest,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var key = idempotencyKey
            ?? httpRequest.Headers[IdempotencyKeyHeader].FirstOrDefault()
            ?? body?.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [IdempotencyKeyHeader] = ["Idempotency-Key header (or body.idempotencyKey) is required."]
            });
        }

        var correlationId = httpRequest.Headers["X-Correlation-ID"].FirstOrDefault();

        var command = new RetryFailedLicenseExtensionsCommand(
            requestId,
            key.Trim(),
            body?.Notes,
            body?.LogicalOperationIds,
            correlationId);

        var result = await sender.Send(command, cancellationToken);

        // Idempotent create semantics: lost-response replay still returns 201 Created
        // for the same Location / prior retry id.
        return Results.Created($"/api/v1/license-extension-requests/{result.Id}", result);
    }
}

public sealed record RetryFailedRequestBody(
    string? Notes = null,
    IReadOnlyList<Guid>? LogicalOperationIds = null,
    string? IdempotencyKey = null);
