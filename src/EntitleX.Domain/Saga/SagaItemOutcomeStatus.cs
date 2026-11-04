namespace EntitleX.Domain.Saga;

/// <summary>Per-item fulfillment outcome tracked by the saga (forward recovery).</summary>
public enum SagaItemOutcomeStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Uncertain = 3
}
