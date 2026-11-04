using EntitleX.Api.Endpoints;
using EntitleX.Api.Infrastructure;
using EntitleX.Api.Middleware;
using EntitleX.Application;
using EntitleX.Infrastructure;
using EntitleX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
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

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "EntitleX API",
            Version = "v1",
            Description = "Enterprise license-extension control plane (Phase 3)."
        });
    });

    builder.Services.AddProblemDetails();

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseEntitleXExceptionHandler();

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
    }));

    app.MapHealthChecks("/health/ready");
    app.MapHealthChecks("/health/providers", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("provider")
    });

    app.MapLicenseExtensionEndpoints();
    app.MapEntitlementAggregationEndpoints();

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
