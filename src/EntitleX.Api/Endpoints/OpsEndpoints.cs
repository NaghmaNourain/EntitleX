using EntitleX.Application.Ops.GetOpsSummary;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

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
}
