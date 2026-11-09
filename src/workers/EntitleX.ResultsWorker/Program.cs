using EntitleX.BuildingBlocks.Observability;
using EntitleX.Infrastructure;
using EntitleX.ResultsWorker;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddEntitleXSerilog(builder.Configuration, "EntitleX.ResultsWorker");
    builder.Services.AddEntitleXOpenTelemetry(
        builder.Configuration,
        "EntitleX.ResultsWorker",
        isAspNetCoreHost: false);
    builder.Services.AddInfrastructureForWorkers(builder.Configuration);
    builder.Services.AddHostedService<ProviderResultsWorker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ResultsWorker terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
