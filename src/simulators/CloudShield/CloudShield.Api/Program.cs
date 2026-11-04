using CloudShield.Api.Endpoints;
using CloudShield.Application;
using CloudShield.Infrastructure;
using CloudShield.Infrastructure.Persistence;
using CloudShield.Infrastructure.Seeding;
using EntitleX.BuildingBlocks.Observability;
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
        .Enrich.WithProperty("Service", "CloudShield.Api")
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .WriteTo.Console());

    builder.Services.AddEntitleXOpenTelemetry(builder.Configuration, "CloudShield.Api", isAspNetCoreHost: true);
    builder.Services.AddCloudShieldApplication();
    builder.Services.AddCloudShieldInfrastructure(builder.Configuration);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<CloudShieldDbContext>("cloudshield-db");

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.MapGet("/health", () => Results.Ok(new
    {
        status = "Healthy",
        service = "CloudShield.Api",
        timestampUtc = DateTimeOffset.UtcNow
    }));

    app.MapHealthChecks("/health/ready");
    app.MapEntitleXPrometheusScrapingEndpoint();
    app.MapCloudShieldEntitlementEndpoints();

    if (ShouldMigrateOnStartup(app.Configuration))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudShieldDbContext>();
        await db.Database.MigrateAsync();
    }

    if (app.Configuration.GetValue("Database:SeedDemoData", defaultValue: false))
    {
        await CloudShieldDemoDataSeeder.SeedAsync(app.Services);
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "CloudShield.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static bool ShouldMigrateOnStartup(IConfiguration configuration) =>
    configuration.GetValue("Database:MigrateOnStartup", defaultValue: false);

public partial class Program;
