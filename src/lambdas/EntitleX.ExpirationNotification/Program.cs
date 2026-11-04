using EntitleX.ExpirationNotification;
using EntitleX.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructureForLambdas(builder.Configuration);
builder.Services.AddSingleton<Function>();

var host = builder.Build();

// Optional: --provider CloudShield | NetworkGuard (EventBridge schedule stubs)
var providerArgIndex = Array.FindIndex(
    args,
    a => string.Equals(a, "--provider", StringComparison.OrdinalIgnoreCase));
IReadOnlyCollection<string>? providers = null;
if (providerArgIndex >= 0 && providerArgIndex + 1 < args.Length)
{
    providers = [args[providerArgIndex + 1]];
}
else
{
    var envProvider = Environment.GetEnvironmentVariable("ENTITLEX_EXPIRATION_PROVIDER");
    if (!string.IsNullOrWhiteSpace(envProvider))
    {
        providers = [envProvider.Trim()];
    }
}

var runOnce = !args.Contains("--loop", StringComparer.OrdinalIgnoreCase);

if (runOnce)
{
    var function = host.Services.GetRequiredService<Function>();
    await function.HandlerAsync(providers);
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExpirationNotificationHost");
while (!cts.IsCancellationRequested)
{
    try
    {
        var function = host.Services.GetRequiredService<Function>();
        await function.HandlerAsync(providers, cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Expiration notification loop iteration failed");
    }

    await Task.Delay(TimeSpan.FromHours(1), cts.Token);
}
