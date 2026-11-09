using EntitleX.Api.Infrastructure;
using EntitleX.Api.Middleware;
using EntitleX.Application.EntitlementAggregation.SearchCustomerEntitlements;
using EntitleX.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EntitleX.Api.Endpoints;

public static class EntitlementAggregationEndpoints
{
    public static IEndpointRouteBuilder MapEntitlementAggregationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/entitlements")
            .WithTags("EntitlementAggregation");

        group.MapGet("/search", SearchAsync)
            .WithName("SearchCustomerEntitlements")
            .WithOpenApi()
            .RequireAuthorization(AuthorizationPolicies.CanSearchEntitlements)
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromQuery] string customerId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] int? take,
        [FromQuery] int? skip,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdMiddleware.HeaderName] as string;

        // No paging args → full aggregation (SPA may page client-side). PageSize=0 = unpaged.
        if (page is null && pageSize is null && take is null && skip is null)
        {
            var unpaged = await sender.Send(
                new SearchCustomerEntitlementsQuery(customerId, correlationId, Page: 1, PageSize: 0),
                cancellationToken);
            return Results.Ok(unpaged);
        }

        var paging = Pagination.Normalize(page, pageSize, take, skip);
        var result = await sender.Send(
            new SearchCustomerEntitlementsQuery(
                customerId,
                correlationId,
                paging.Page,
                paging.PageSize),
            cancellationToken);

        return Results.Ok(result);
    }
}
