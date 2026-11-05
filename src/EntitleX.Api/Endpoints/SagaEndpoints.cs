using EntitleX.Application.Saga.GetSaga;
using EntitleX.Infrastructure.Identity;
using MediatR;

namespace EntitleX.Api.Endpoints;

public static class SagaEndpoints
{
    public static IEndpointRouteBuilder MapSagaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/license-extension-requests/{requestId:guid}/saga")
            .WithTags("Saga");

        group.MapGet("/", GetAsync)
            .WithName("GetLicenseExtensionSaga")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanReadApprovals)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid requestId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSagaQuery(requestId), cancellationToken);
        return Results.Ok(result);
    }
}
