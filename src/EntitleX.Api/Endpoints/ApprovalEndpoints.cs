using EntitleX.Application.Approval.DecideApproval;
using EntitleX.Application.Approval.GetApproval;
using EntitleX.Application.Approval.StartApproval;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

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
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> StartAsync(
        Guid requestId,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[Middleware.CorrelationIdMiddleware.HeaderName] as string;
        var result = await sender.Send(
            new StartApprovalCommand(requestId, correlationId),
            cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[Middleware.CorrelationIdMiddleware.HeaderName] as string
            ?? body.CorrelationId;

        var result = await sender.Send(
            new DecideApprovalCommand(requestId, body.Outcome, body.Comment, correlationId),
            cancellationToken);

        return Results.Ok(result);
    }
}

public sealed record DecideApprovalRequest(
    string Outcome,
    string? Comment = null,
    string? CorrelationId = null);
