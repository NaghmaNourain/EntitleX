using CloudShield.Application.Abstractions;
using CloudShield.Application.Entitlements;
using CloudShield.Application.Models;
using CloudShield.Application.Operations;
using CloudShield.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CloudShield.Api.Endpoints;

public static class EntitlementEndpoints
{
    public static IEndpointRouteBuilder MapCloudShieldEntitlementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("CloudShieldEntitlements");

        group.MapGet("/accounts/{customerAccountId}/entitlements", DiscoverAsync)
            .WithName("DiscoverCloudShieldEntitlements")
            .WithOpenApi();

        group.MapGet("/entitlements/{entitlementKey}", GetByKeyAsync)
            .WithName("GetCloudShieldEntitlement")
            .WithOpenApi();

        group.MapPost("/entitlements/{entitlementKey}/license-extensions", SubmitExtensionAsync)
            .WithName("SubmitCloudShieldLicenseExtension")
            .WithOpenApi();

        group.MapGet("/license-operations/{logicalOperationId:guid}", GetOperationAsync)
            .WithName("GetCloudShieldLicenseOperation")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> DiscoverAsync(
        string customerAccountId,
        EntitlementQueries queries,
        CancellationToken cancellationToken)
    {
        var results = await queries.DiscoverByCustomerAsync(customerAccountId, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GetByKeyAsync(
        string entitlementKey,
        EntitlementQueries queries,
        CancellationToken cancellationToken)
    {
        var result = await queries.GetByKeyAsync(entitlementKey, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> SubmitExtensionAsync(
        string entitlementKey,
        [FromBody] SubmitLicenseExtensionRequest request,
        ILicenseExtensionApplier applier,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await applier.ApplyAsync(entitlementKey, request, cancellationToken);
            return result.AlreadyApplied
                ? Results.Ok(result)
                : Results.Created($"/api/v1/license-operations/{result.LogicalOperationId}", result);
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
