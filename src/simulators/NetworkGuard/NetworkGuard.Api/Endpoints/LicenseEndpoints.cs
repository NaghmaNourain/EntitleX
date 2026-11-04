using Microsoft.AspNetCore.Mvc;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Application.Licenses;
using NetworkGuard.Application.Models;
using NetworkGuard.Application.Operations;
using NetworkGuard.Domain.Common;

namespace NetworkGuard.Api.Endpoints;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapNetworkGuardLicenseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("NetworkGuardLicenses");

        group.MapGet("/tenants/{tenantExternalId}/licenses", DiscoverAsync)
            .WithName("DiscoverNetworkGuardLicenses")
            .WithOpenApi();

        group.MapGet("/licenses/{licenseId}", GetByIdAsync)
            .WithName("GetNetworkGuardLicense")
            .WithOpenApi();

        group.MapPost("/licenses/{licenseId}/extensions", SubmitExtensionAsync)
            .WithName("SubmitNetworkGuardLicenseExtension")
            .WithOpenApi();

        group.MapGet("/operations/{logicalOperationId:guid}", GetOperationAsync)
            .WithName("GetNetworkGuardOperation")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> DiscoverAsync(
        string tenantExternalId,
        LicenseQueries queries,
        CancellationToken cancellationToken)
    {
        var results = await queries.DiscoverByTenantAsync(tenantExternalId, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetByIdAsync(
        string licenseId,
        LicenseQueries queries,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetByLicenseIdAsync(licenseId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> SubmitExtensionAsync(
        string licenseId,
        [FromBody] SubmitLicenseExtensionRequest request,
        ILicenseExtensionApplier applier,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await applier.ApplyAsync(licenseId, request, cancellationToken);
            return result.AlreadyApplied
                ? Results.Ok(result)
                : Results.Created($"/api/v1/operations/{result.LogicalOperationId}", result);
        }
        catch (DomainException ex)
        {
            return Results.UnprocessableEntity(new { title = "Domain rule violated", detail = ex.Message });
        }
    }

    private static async Task<IResult> GetOperationAsync(
        Guid logicalOperationId,
        OperationQueries queries,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetByLogicalOperationIdAsync(logicalOperationId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
