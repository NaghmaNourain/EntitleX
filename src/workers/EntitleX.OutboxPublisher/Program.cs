using EntitleX.BuildingBlocks.Observability;
using EntitleX.Infrastructure;
using EntitleX.OutboxPublisher;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddEntitleXSerilog(builder.Configuration, "EntitleX.OutboxPublisher");
    builder.Services.AddEntitleXOpenTelemetry(
        builder.Configuration,
        "EntitleX.OutboxPublisher",
        isAspNetCoreHost: false);
    builder.Services.AddInfrastructureForWorkers(builder.Configuration);
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "OutboxPublisher terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
