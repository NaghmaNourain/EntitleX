using System.Diagnostics;

namespace EntitleX.BuildingBlocks.Observability;

public static class SagaTelemetry
{
    public static Activity? StartTransition(string operation, string? fromState = null, string? toState = null)
    {
        var activity = EntitleXActivitySources.Saga.StartActivity(
            $"saga.{operation}",
            ActivityKind.Internal);

        if (fromState is not null)
        {
            activity?.SetTag("saga.from_state", fromState);
        }

        if (toState is not null)
        {
            activity?.SetTag("saga.to_state", toState);
        }

        return activity;
    }

    public static void RecordTransition(string fromState, string toState)
    {
        EntitleXMeters.SagaTransitions.Add(
            1,
            new KeyValuePair<string, object?>("from_state", fromState),
            new KeyValuePair<string, object?>("to_state", toState));
    }

    public static void RecordFulfillmentOutcome(string outcome)
    {
        EntitleXMeters.FulfillmentOutcomes.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome));
    }
}
