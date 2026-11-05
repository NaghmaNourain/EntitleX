using EntitleX.Api.Middleware;
using EntitleX.Application.EntitlementAggregation.SearchCustomerEntitlements;
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
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromQuery] string customerId,
        HttpContext httpContext,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdMiddleware.HeaderName] as string;

        var result = await sender.Send(
            new SearchCustomerEntitlementsQuery(customerId, correlationId),
            cancellationToken);

        return Results.Ok(result);
    }
}
