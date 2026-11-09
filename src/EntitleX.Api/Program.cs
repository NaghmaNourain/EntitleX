using EntitleX.Api.Endpoints;
using EntitleX.Api.Infrastructure;
using EntitleX.Api.Middleware;
using EntitleX.Application;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.BuildingBlocks.Security;
using EntitleX.Infrastructure;
using EntitleX.Infrastructure.Identity;
using EntitleX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "EntitleX.Api")
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .WriteTo.Console());

    builder.Services.AddEntitleXOpenTelemetry(builder.Configuration, "EntitleX.Api", isAspNetCoreHost: true);
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
    builder.Services.Configure<MetricsBasicAuthOptions>(
        builder.Configuration.GetSection(MetricsBasicAuthOptions.SectionName));

    if (builder.Environment.IsDevelopment())
    {
        // Local Keycloak and DevToken use HTTP metadata / loopback.
        builder.Services.PostConfigureAll<JwtBearerOptions>(options =>
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
            Description = "Enterprise license-extension control plane (Phase 11 — OpenTelemetry + metrics)."
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
    builder.Services.AddEntitleXRateLimiting(builder.Configuration);

    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("FrontendDev", policy =>
                policy.WithOrigins(
                        "http://localhost:5173",
                        "http://127.0.0.1:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });
    }

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseEntitleXExceptionHandler();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
        app.UseHsts();
        app.UseEntitleXSecurityHeaders();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseCors("FrontendDev");
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseEntitleXRateLimiting();

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
    app.MapEntitleXPrometheusScrapingEndpoint()
        .RequireEntitleXMetricsAuth(app.Environment, app.Configuration);

    app.MapLicenseExtensionEndpoints();
    app.MapLicenseExtensionRetryEndpoints();
    app.MapEntitlementAggregationEndpoints();
    app.MapApprovalEndpoints();
    app.MapPendingApprovalEndpoints();
    app.MapSagaEndpoints();
    app.MapReconciliationEndpoints();
    app.MapInsightsEndpoints();
    app.MapAuditEndpoints();
    app.MapOpsEndpoints();

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
