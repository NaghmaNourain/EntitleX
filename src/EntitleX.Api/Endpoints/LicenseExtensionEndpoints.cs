using EntitleX.Application.LicenseExtension.CreateLicenseExtensionRequest;
using EntitleX.Application.LicenseExtension.GetLicenseExtensionRequest;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EntitleX.Api.Endpoints;

public static class LicenseExtensionEndpoints
{
    public static IEndpointRouteBuilder MapLicenseExtensionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/license-extension-requests")
            .WithTags("LicenseExtensionRequests");

        group.MapPost("/", CreateAsync)
            .WithName("CreateLicenseExtensionRequest")
            .WithOpenApi()
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetLicenseExtensionRequest")
            .WithOpenApi()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateLicenseExtensionRequestCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return Results.Created($"/api/v1/license-extension-requests/{result.Id}", result);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetLicenseExtensionRequestQuery(id), cancellationToken);
        return Results.Ok(result);
    }
}
