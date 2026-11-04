using EntitleX.BuildingBlocks.Observability;
using EntitleX.FulfillmentWorker;
using EntitleX.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddEntitleXSerilog(builder.Configuration, "EntitleX.FulfillmentWorker");
    builder.Services.AddEntitleXOpenTelemetry(
        builder.Configuration,
        "EntitleX.FulfillmentWorker",
        isAspNetCoreHost: false);
    builder.Services.AddInfrastructureForWorkers(builder.Configuration);
    builder.Services.AddScoped<FulfillmentCommandProcessor>();
    builder.Services.AddHostedService<FulfillmentSqsWorker>();
    builder.Services.AddHostedService<ProviderResultsWorker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "FulfillmentWorker terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
