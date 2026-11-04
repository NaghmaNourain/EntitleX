using EntitleX.Infrastructure;
using EntitleX.OutboxPublisher;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((_, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "EntitleX.OutboxPublisher")
        .WriteTo.Console());

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
