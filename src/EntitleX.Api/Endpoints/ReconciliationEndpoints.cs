using EntitleX.Application.Reconciliation.ManualReconcile;
using EntitleX.Infrastructure.Identity;
using MediatR;

namespace EntitleX.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/reconciliation")
            .WithTags("Reconciliation");

        group.MapPost("/requests/{requestId:guid}", ManualReconcileAsync)
            .WithName("ManualReconcileLicenseExtensionRequest")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanManageReconciliation)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ManualReconcileAsync(
        Guid requestId,
        HttpRequest httpRequest,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var correlationId = httpRequest.Headers["X-Correlation-ID"].FirstOrDefault();
        var result = await sender.Send(
            new ManualReconcileCommand(requestId, correlationId),
            cancellationToken);
        return Results.Ok(result);
    }
}
