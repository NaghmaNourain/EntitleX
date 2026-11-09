using EntitleX.Api.Infrastructure;
using EntitleX.Application.Abstractions;
using EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;
using EntitleX.Application.LicenseExtension.GetLicenseExtensionRequest;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EntitleX.Api.Endpoints;

public static class LicenseExtensionEndpoints
{
    public static IEndpointRouteBuilder MapLicenseExtensionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/license-extension-requests")
            .WithTags("LicenseExtensionRequests");

        group.MapPost("/", CreateAsync)
            .WithName("CreateLicenseExtensionRequest")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanCreateRequests)
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetLicenseExtensionRequest")
            .WithOpenApi()
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateLicenseExtensionRequestCommand command,
        HttpRequest httpRequest,
        ISender sender,
        IApiIdempotencyStore idempotency,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var key = IdempotencyKeyHelper.ReadKey(httpRequest);
        if (key is null)
        {
            return IdempotencyKeyHelper.MissingKeyResult();
        }

        var fingerprint = IdempotencyKeyHelper.Fingerprint(new
        {
            command.ExtensionNumber,
            Items = command.Items
                .OrderBy(i => i.EntitlementId, StringComparer.Ordinal)
                .Select(i => new
                {
                    i.EntitlementId,
                    i.Provider,
                    i.RequestedExpiryUtc
                })
        });

        var actor = currentUser.UserId;
        var existing = await idempotency.FindAsync(key, cancellationToken);
        if (existing is not null)
        {
            IdempotencyKeyHelper.EnsureReplayMatches(
                existing,
                ApiIdempotencyOperations.CreateLicenseExtensionRequest,
                actor,
                fingerprint);

            var prior = await sender.Send(
                new GetLicenseExtensionRequestQuery(Guid.Parse(existing.ResourceId)),
                cancellationToken);
            return Results.Created($"/api/v1/license-extension-requests/{prior.Id}", prior);
        }

        var result = await sender.Send(command, cancellationToken);

        var recorded = await idempotency.TryRecordAsync(
            key,
            ApiIdempotencyOperations.CreateLicenseExtensionRequest,
            actor,
            fingerprint,
            result.Id.ToString("D"),
            cancellationToken);

        if (!recorded)
        {
            var raced = await idempotency.FindAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency race without stored record.");
            IdempotencyKeyHelper.EnsureReplayMatches(
                raced,
                ApiIdempotencyOperations.CreateLicenseExtensionRequest,
                actor,
                fingerprint);
            var prior = await sender.Send(
                new GetLicenseExtensionRequestQuery(Guid.Parse(raced.ResourceId)),
                cancellationToken);
            return Results.Created($"/api/v1/license-extension-requests/{prior.Id}", prior);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/license-extension-requests/{result.Id}", result);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetLicenseExtensionRequestQuery(id), cancellationToken);
        return Results.Ok(result);
    }
}
