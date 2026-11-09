using EntitleX.Api.Infrastructure;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Approval.DecideApproval;
using EntitleX.Application.Approval.GetApproval;
using EntitleX.Application.Approval.StartApproval;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EntitleX.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/license-extension-requests/{requestId:guid}/approval")
            .WithTags("Approvals");

        group.MapPost("/start", StartAsync)
            .WithName("StartApproval")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanCreateRequests)
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/", GetAsync)
            .WithName("GetApproval")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanReadApprovals)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/decisions", DecideAsync)
            .WithName("DecideApproval")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanDecideApprovals)
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> StartAsync(
        Guid requestId,
        HttpContext httpContext,
        ISender sender,
        IApiIdempotencyStore idempotency,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var key = IdempotencyKeyHelper.ReadKey(httpContext.Request);
        if (key is null)
        {
            return IdempotencyKeyHelper.MissingKeyResult();
        }

        var fingerprint = IdempotencyKeyHelper.Fingerprint(new { requestId });
        var actor = currentUser.UserId;
        var existing = await idempotency.FindAsync(key, cancellationToken);
        if (existing is not null)
        {
            IdempotencyKeyHelper.EnsureReplayMatches(
                existing,
                ApiIdempotencyOperations.StartApproval,
                actor,
                fingerprint);
            var prior = await sender.Send(new GetApprovalQuery(requestId), cancellationToken);
            return Results.Ok(prior);
        }

        var correlationId = httpContext.Items[Middleware.CorrelationIdMiddleware.HeaderName] as string;
        var result = await sender.Send(
            new StartApprovalCommand(requestId, correlationId),
            cancellationToken);

        var recorded = await idempotency.TryRecordAsync(
            key,
            ApiIdempotencyOperations.StartApproval,
            actor,
            fingerprint,
            requestId.ToString("D"),
            cancellationToken);

        if (!recorded)
        {
            var raced = await idempotency.FindAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency race without stored record.");
            IdempotencyKeyHelper.EnsureReplayMatches(
                raced,
                ApiIdempotencyOperations.StartApproval,
                actor,
                fingerprint);
            return Results.Ok(await sender.Send(new GetApprovalQuery(requestId), cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(
        Guid requestId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetApprovalQuery(requestId), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> DecideAsync(
        Guid requestId,
        [FromBody] DecideApprovalRequest body,
        HttpContext httpContext,
        ISender sender,
        IApiIdempotencyStore idempotency,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        var key = IdempotencyKeyHelper.ReadKey(httpContext.Request);
        if (key is null)
        {
            return IdempotencyKeyHelper.MissingKeyResult();
        }

        var fingerprint = IdempotencyKeyHelper.Fingerprint(new
        {
            requestId,
            body.Outcome,
            Comment = body.Comment ?? string.Empty
        });
        var actor = currentUser.UserId;
        var existing = await idempotency.FindAsync(key, cancellationToken);
        if (existing is not null)
        {
            IdempotencyKeyHelper.EnsureReplayMatches(
                existing,
                ApiIdempotencyOperations.DecideApproval,
                actor,
                fingerprint);
            return Results.Ok(await sender.Send(new GetApprovalQuery(requestId), cancellationToken));
        }

        var correlationId = httpContext.Items[Middleware.CorrelationIdMiddleware.HeaderName] as string
            ?? body.CorrelationId;

        var result = await sender.Send(
            new DecideApprovalCommand(requestId, body.Outcome, body.Comment, correlationId),
            cancellationToken);

        var recorded = await idempotency.TryRecordAsync(
            key,
            ApiIdempotencyOperations.DecideApproval,
            actor,
            fingerprint,
            requestId.ToString("D"),
            cancellationToken);

        if (!recorded)
        {
            var raced = await idempotency.FindAsync(key, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency race without stored record.");
            IdempotencyKeyHelper.EnsureReplayMatches(
                raced,
                ApiIdempotencyOperations.DecideApproval,
                actor,
                fingerprint);
            return Results.Ok(await sender.Send(new GetApprovalQuery(requestId), cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Results.Ok(result);
    }
}

public sealed record DecideApprovalRequest(
    string Outcome,
    string? Comment = null,
    string? CorrelationId = null);
