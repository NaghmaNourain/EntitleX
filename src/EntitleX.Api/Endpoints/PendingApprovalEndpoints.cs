using EntitleX.Api.Infrastructure;
using EntitleX.Application.Approval.ListPendingApprovals;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EntitleX.Api.Endpoints;

public static class PendingApprovalEndpoints
{
    public static IEndpointRouteBuilder MapPendingApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/approvals")
            .WithTags("Approvals");

        group.MapGet("/pending", ListPendingAsync)
            .WithName("ListPendingApprovals")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanDecideApprovals)
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> ListPendingAsync(
        [FromQuery] string? role,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] int? take,
        [FromQuery] int? skip,
        [FromQuery] DateTimeOffset? afterCreatedAtUtc,
        [FromQuery] Guid? afterApprovalCaseId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var paging = Pagination.Normalize(page, pageSize, take, skip);

        var result = await sender.Send(
            new ListPendingApprovalsQuery(
                role,
                paging.PageSize,
                paging.Skip,
                afterCreatedAtUtc,
                afterApprovalCaseId,
                paging.Page,
                paging.PageSize),
            cancellationToken);
        return Results.Ok(result);
    }
}
