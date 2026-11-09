namespace EntitleX.Messaging.Contracts;

public sealed record FulfillmentCommandV1(
    Guid RequestId,
    string ExtensionNumber,
    IReadOnlyList<FulfillmentItemV1> Items);

public sealed record FulfillmentItemV1(
    Guid RequestItemId,
    string EntitlementId,
    string Provider,
    Guid LogicalOperationId,
    Guid AttemptId,
    DateTimeOffset CurrentExpiryUtc,
    DateTimeOffset RequestedExpiryUtc);
