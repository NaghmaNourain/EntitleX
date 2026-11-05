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
        [FromQuery] int take,
        [FromQuery] int skip,
        [FromQuery] DateTimeOffset? afterCreatedAtUtc,
        [FromQuery] Guid? afterApprovalCaseId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ListPendingApprovalsQuery(
                role,
                take <= 0 ? 50 : take,
                skip < 0 ? 0 : skip,
                afterCreatedAtUtc,
                afterApprovalCaseId),
            cancellationToken);
        return Results.Ok(result);
    }
}
