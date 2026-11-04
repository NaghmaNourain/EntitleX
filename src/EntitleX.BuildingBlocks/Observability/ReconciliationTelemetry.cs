using System.Diagnostics;

namespace EntitleX.BuildingBlocks.Observability;

public static class ReconciliationTelemetry
{
    public static Activity? StartPass(string operation = "reconcile")
    {
        return EntitleXActivitySources.Reconciliation.StartActivity(
            $"reconciliation.{operation}",
            ActivityKind.Internal);
    }

    public static void RecordAction(string action)
    {
        EntitleXMeters.ReconciliationActions.Add(
            1,
            new KeyValuePair<string, object?>("action", action));
    }

    public static void SetBacklog(long count) =>
        EntitleXMeters.ReconciliationBacklogValue = Math.Max(0, count);
}
