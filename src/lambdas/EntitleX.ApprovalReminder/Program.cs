using EntitleX.ApprovalReminder;
using EntitleX.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructureForLambdas(builder.Configuration);
builder.Services.AddSingleton<Function>();

var host = builder.Build();

var runOnce = args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase))
    || string.Equals(
        Environment.GetEnvironmentVariable("ENTITLEX_LAMBDA_RUN_ONCE"),
        "1",
        StringComparison.OrdinalIgnoreCase);

if (runOnce || !args.Contains("--loop", StringComparer.OrdinalIgnoreCase))
{
    // Default local host behaviour: run one pass then exit (EventBridge-compatible).
    var function = host.Services.GetRequiredService<Function>();
    await function.HandlerAsync();
    return;
}

// Optional long-running local loop for manual soak tests: --loop
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApprovalReminderHost");
while (!cts.IsCancellationRequested)
{
    try
    {
        var function = host.Services.GetRequiredService<Function>();
        await function.HandlerAsync(cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Approval reminder loop iteration failed");
    }

    await Task.Delay(TimeSpan.FromHours(1), cts.Token);
}
