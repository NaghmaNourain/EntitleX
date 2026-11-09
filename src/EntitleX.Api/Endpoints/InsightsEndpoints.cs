using EntitleX.Application.Insights.ExplainRequest;
using EntitleX.Application.Insights.ReconcileRca;
using EntitleX.Infrastructure.Identity;
using MediatR;

namespace EntitleX.Api.Endpoints;

public static class InsightsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/license-extension-requests/{requestId:guid}/explain", ExplainAsync)
            .WithName("ExplainLicenseExtensionRequest")
            .WithTags("Insights")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanReadApprovals)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/admin/reconciliation/requests/{requestId:guid}/rca", RcaAsync)
            .WithName("ReconcileRcaLicenseExtensionRequest")
            .WithTags("Insights")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanManageReconciliation)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ExplainAsync(
        Guid requestId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ExplainRequestQuery(requestId), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> RcaAsync(
        Guid requestId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReconcileRcaQuery(requestId), cancellationToken);
        return Results.Ok(result);
    }
}
