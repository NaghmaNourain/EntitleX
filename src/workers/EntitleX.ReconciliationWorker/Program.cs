using EntitleX.BuildingBlocks.Observability;
using EntitleX.Infrastructure;
using EntitleX.ReconciliationWorker;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddEntitleXSerilog(builder.Configuration, "EntitleX.ReconciliationWorker");
    builder.Services.AddEntitleXOpenTelemetry(
        builder.Configuration,
        "EntitleX.ReconciliationWorker",
        isAspNetCoreHost: false);
    builder.Services.AddInfrastructureForWorkers(builder.Configuration);
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ReconciliationWorker terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
