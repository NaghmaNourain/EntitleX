namespace EntitleX.BuildingBlocks.Observability;

public static class ApprovalTelemetry
{
    public static void RecordDecisionLatency(TimeSpan latency, string outcome, string stageRole)
    {
        EntitleXMeters.ApprovalLatencySeconds.Record(
            Math.Max(0, latency.TotalSeconds),
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("stage_role", stageRole));
    }
}
