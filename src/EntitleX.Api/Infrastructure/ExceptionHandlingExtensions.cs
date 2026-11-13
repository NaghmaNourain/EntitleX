using EntitleX.Application.Common.Exceptions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("EntitleX.Api.ExceptionHandler");

                if (exception is not null)
                {
                    var statusHint = MapException(exception).StatusCode;
                    if (statusHint >= StatusCodes.Status500InternalServerError)
                    {
                        logger.LogError(
                            exception,
                            "Unhandled exception for {Method} {Path}",
                            context.Request.Method,
                            context.Request.Path);
                    }
                    else
                    {
                        logger.LogWarning(
                            exception,
                            "Request failed with {StatusCode} for {Method} {Path}",
                            statusHint,
                            context.Request.Method,
                            context.Request.Path);
                    }
                }

                var (statusCode, title, detail, errors) = MapException(exception);

                // Surface the real message in Development so lab 500s are diagnosable in the SPA.
                if (statusCode >= StatusCodes.Status500InternalServerError
                    && exception is not null
                    && context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
                {
                    detail = exception.GetBaseException().Message;
                }

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
            DbUpdateException db when IsUniqueViolation(db) => (
                StatusCodes.Status409Conflict,
                "Conflict",
                MapUniqueViolationDetail(db),
                null),
            PostgresException pg when pg.SqlState == PostgresErrorCodes.UniqueViolation => (
                StatusCodes.Status409Conflict,
                "Conflict",
                string.IsNullOrWhiteSpace(pg.Detail) ? "A unique constraint was violated." : pg.Detail,
                null),
            _ => (
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred",
                "An unexpected error occurred while processing the request.",
                null)
        };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string MapUniqueViolationDetail(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg
            && !string.IsNullOrWhiteSpace(pg.ConstraintName)
            && pg.ConstraintName.Contains("extension_number", StringComparison.OrdinalIgnoreCase))
        {
            return "A license-extension request with this ExtensionNumber already exists.";
        }

        return "A unique constraint was violated.";
    }
}
