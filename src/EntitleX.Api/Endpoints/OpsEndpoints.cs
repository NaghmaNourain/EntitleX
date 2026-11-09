using EntitleX.Api.Infrastructure;
using EntitleX.Application.Ops.GetOpsSummary;
using EntitleX.Application.Ops.ReplayPoisonOutbox;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EntitleX.Api.Endpoints;

public static class OpsEndpoints
{
    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/ops")
            .WithTags("Operations");

        group.MapGet("/summary", GetSummaryAsync)
            .WithName("GetOpsSummary")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanManageReconciliation)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/outbox/poison/replay", ReplayPoisonAsync)
            .WithName("ReplayPoisonOutbox")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanManageReconciliation)
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        [FromQuery] int take,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetOpsSummaryQuery(take <= 0 ? 25 : take),
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ReplayPoisonAsync(
        [FromQuery] Guid? outboxMessageId,
        [FromQuery] int take,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReplayPoisonOutboxCommand(outboxMessageId, take <= 0 ? 25 : take),
            cancellationToken);
        return Results.Ok(result);
    }
}
