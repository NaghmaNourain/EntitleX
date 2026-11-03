using Microsoft.EntityFrameworkCore;
using NetworkGuard.Api.Endpoints;
using NetworkGuard.Application;
using NetworkGuard.Infrastructure;
using NetworkGuard.Infrastructure.Persistence;
using NetworkGuard.Infrastructure.Seeding;
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
        .Enrich.WithProperty("Service", "NetworkGuard.Api")
        .WriteTo.Console());

    builder.Services.AddNetworkGuardApplication();
    builder.Services.AddNetworkGuardInfrastructure(builder.Configuration);

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<NetworkGuardDbContext>("networkguard-db");

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
        service = "NetworkGuard.Api",
        timestampUtc = DateTimeOffset.UtcNow
    }));

    app.MapHealthChecks("/health/ready");
    app.MapNetworkGuardLicenseEndpoints();

    if (ShouldMigrateOnStartup(app.Configuration))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NetworkGuardDbContext>();
        await db.Database.MigrateAsync();
    }

    if (app.Configuration.GetValue("Database:SeedDemoData", defaultValue: false))
    {
        await NetworkGuardDemoDataSeeder.SeedAsync(app.Services);
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "NetworkGuard.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static bool ShouldMigrateOnStartup(IConfiguration configuration) =>
    configuration.GetValue("Database:MigrateOnStartup", defaultValue: false);

public partial class Program;
