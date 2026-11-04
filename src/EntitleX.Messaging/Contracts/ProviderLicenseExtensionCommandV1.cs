namespace EntitleX.Messaging.Contracts;

public sealed record ProviderLicenseExtensionCommandV1(
    Guid RequestId,
    Guid RequestItemId,
    string EntitlementId,
    string Provider,
    Guid LogicalOperationId,
    Guid AttemptId,
    DateTimeOffset NewExpiryUtc);
