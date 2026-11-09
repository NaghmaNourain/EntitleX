using EntitleX.Api.Infrastructure;
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
        int? page,
        int? pageSize,
        int? take,
        int? skip,
        DateTimeOffset? afterOccurredAtUtc,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        var paging = Pagination.Normalize(page, pageSize, take, skip);

        var result = await sender.Send(
            new GetRequestAuditQuery(
                requestId,
                paging.PageSize,
                afterOccurredAtUtc,
                afterId,
                paging.Page,
                paging.PageSize),
            cancellationToken);
        return Results.Ok(result);
    }
}
