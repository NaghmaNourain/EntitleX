using EntitleX.BuildingBlocks.Observability;
using EntitleX.Infrastructure;
using EntitleX.NotificationService;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddEntitleXSerilog(builder.Configuration, "EntitleX.NotificationService");
    builder.Services.AddEntitleXOpenTelemetry(
        builder.Configuration,
        "EntitleX.NotificationService",
        isAspNetCoreHost: false);
    builder.Services.AddEntitleXEmail(builder.Configuration);
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NotificationService terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
