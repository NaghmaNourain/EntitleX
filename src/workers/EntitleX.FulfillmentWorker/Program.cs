using EntitleX.BuildingBlocks.Observability;
using EntitleX.FulfillmentWorker;
using EntitleX.Infrastructure;
using EntitleX.Messaging.Options;
using Microsoft.Extensions.Options;
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

    // Legacy SQS→Kafka hop (off by default — PreferDirectKafka writes Kafka outbox at approval).
    // Provider results live in EntitleX.ResultsWorker.
    var enableSqs = builder.Configuration.GetValue("Messaging:Fulfillment:EnableSqsDispatcher", false);
    if (enableSqs)
    {
        builder.Services.AddHostedService<FulfillmentSqsWorker>();
    }

    var host = builder.Build();
    var fulfillment = host.Services.GetRequiredService<IOptions<MessagingOptions>>().Value.Fulfillment;
    if (!fulfillment.EnableSqsDispatcher && !enableSqs)
    {
        Log.Information(
            "FulfillmentWorker idle: EnableSqsDispatcher=false. Prefer EntitleX.ResultsWorker for provider results; " +
            "approval path enqueues Kafka via outbox when PreferDirectKafka=true.");
    }

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
