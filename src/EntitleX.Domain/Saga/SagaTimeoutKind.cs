namespace EntitleX.Domain.Saga;

/// <summary>
/// Timeout hook kinds scheduled against a saga. Lambdas / workers invoke
/// <see cref="LicenseExtensionSaga.ApplyTimeout"/> when the deadline elapses.
/// </summary>
public enum SagaTimeoutKind
{
    None = 0,
    ApprovalPending = 1,
    FulfillmentProcessing = 2
}
