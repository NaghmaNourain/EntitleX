using System.Diagnostics;
using EntitleX.BuildingBlocks.Observability;

namespace EntitleX.Messaging.Metrics;

internal static class MessagingTelemetry
{
    public static Activity? StartProduce(string transport, string destination)
    {
        var activity = EntitleXActivitySources.Messaging.StartActivity(
            $"messaging.produce {transport}",
            ActivityKind.Producer);

        activity?.SetTag("messaging.system", transport);
        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.operation", "publish");
        return activity;
    }

    public static Activity? StartConsume(
        string transport,
        string destination,
        ActivityContext parentContext = default)
    {
        Activity? activity;
        if (parentContext != default)
        {
            activity = EntitleXActivitySources.Messaging.StartActivity(
                $"messaging.consume {transport}",
                ActivityKind.Consumer,
                parentContext);
        }
        else
        {
            activity = EntitleXActivitySources.Messaging.StartActivity(
                $"messaging.consume {transport}",
                ActivityKind.Consumer);
        }

        activity?.SetTag("messaging.system", transport);
        activity?.SetTag("messaging.destination", destination);
        activity?.SetTag("messaging.operation", "receive");
        if (parentContext != default)
        {
            activity?.SetTag("messaging.trace_context", "extracted");
        }

        return activity;
    }

    public static void RecordOperation(
        string transport,
        string operation,
        string destination,
        double elapsedSeconds,
        bool success)
    {
        var tags = new TagList
        {
            { "transport", transport },
            { "operation", operation },
            { "destination", destination },
            { "success", success ? "true" : "false" }
        };

        EntitleXMeters.MessagingOperations.Add(1, tags);
        EntitleXMeters.MessagingLatencySeconds.Record(elapsedSeconds, tags);
    }
}
