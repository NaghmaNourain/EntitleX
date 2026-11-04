using EntitleX.Api.Endpoints;
using EntitleX.Api.Infrastructure;
using EntitleX.Api.Middleware;
using EntitleX.Application;
using EntitleX.Infrastructure;
using EntitleX.Infrastructure.Identity;
using EntitleX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "EntitleX.Api")
        .WriteTo.Console());

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.RequireHttpsMetadata = false;
        });
    }

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "EntitleX API",
            Version = "v1",
            Description = "Enterprise license-extension control plane (Phase 4 — approvals + JWT/RBAC)."
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseEntitleXExceptionHandler();

    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.MapGet("/health", () => Results.Ok(new
        {
            status = "Healthy",
            service = "EntitleX.Api",
            timestampUtc = DateTimeOffset.UtcNow
        }))
        .AllowAnonymous();

    app.MapHealthChecks("/health/ready").AllowAnonymous();
    app.MapHealthChecks("/health/providers", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("provider")
    }).AllowAnonymous();

    app.MapLicenseExtensionEndpoints();
    app.MapEntitlementAggregationEndpoints();
    app.MapApprovalEndpoints();

    var jwtOptions = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
    if (app.Environment.IsDevelopment() && jwtOptions.EnableDevTokenEndpoint)
    {
        app.MapDevTokenEndpoints();
    }

    if (ShouldMigrateOnStartup(app.Configuration))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();
        await db.Database.MigrateAsync();
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "EntitleX.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static bool ShouldMigrateOnStartup(IConfiguration configuration) =>
    configuration.GetValue("Database:MigrateOnStartup", defaultValue: false);

public partial class Program;
