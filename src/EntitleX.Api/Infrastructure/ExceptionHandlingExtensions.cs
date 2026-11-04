using EntitleX.Application.Common.Exceptions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EntitleX.Api.Infrastructure;

public static class ExceptionHandlingExtensions
{
    public static IApplicationBuilder UseEntitleXExceptionHandler(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                var exception = feature?.Error;

                var (statusCode, title, detail, errors) = MapException(exception);

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Status = statusCode,
                    Title = title,
                    Detail = detail,
                    Instance = context.Request.Path,
                    Type = $"https://httpstatuses.com/{statusCode}"
                };

                if (errors is not null)
                {
                    problem.Extensions["errors"] = errors;
                }

                await context.Response.WriteAsJsonAsync(problem);
            });
        });

        return app;
    }

    private static (int StatusCode, string Title, string? Detail, object? Errors) MapException(
        Exception? exception) =>
        exception switch
        {
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                "One or more validation errors occurred.",
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                notFound.Message,
                null),
            ConflictException conflict => (
                StatusCodes.Status409Conflict,
                "Conflict",
                conflict.Message,
                null),
            ForbiddenException forbidden => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                forbidden.Message,
                null),
            UnauthorizedApprovalTransitionException unauthorized => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                unauthorized.Message,
                null),
            DomainException domain => (
                StatusCodes.Status422UnprocessableEntity,
                "Domain rule violated",
                domain.Message,
                null),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "An unexpected error occurred while processing the request.",
                null)
        };
}
