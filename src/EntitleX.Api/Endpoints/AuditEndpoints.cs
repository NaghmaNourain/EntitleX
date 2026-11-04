using EntitleX.Application.Audit.GetRequestAudit;
using EntitleX.Infrastructure.Identity;
using MediatR;

namespace EntitleX.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit")
            .WithTags("Audit");

        group.MapGet("/requests/{requestId:guid}", GetByRequestAsync)
            .WithName("GetRequestAudit")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanReadApprovals)
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> GetByRequestAsync(
        Guid requestId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRequestAuditQuery(requestId), cancellationToken);
        return Results.Ok(result);
    }
}
