namespace EntitleX.Messaging.Contracts;

public sealed record ProviderLicenseExtensionCompletedV1(
    Guid RequestId,
    Guid RequestItemId,
    string EntitlementId,
    string Provider,
    Guid LogicalOperationId,
    Guid AttemptId,
    DateTimeOffset PreviousExpiryUtc,
    DateTimeOffset NewExpiryUtc,
    bool AlreadyApplied);

public sealed record ProviderLicenseExtensionFailedV1(
    Guid RequestId,
    Guid RequestItemId,
    string EntitlementId,
    string Provider,
    Guid LogicalOperationId,
    Guid AttemptId,
    string ErrorCode,
    string ErrorMessage);
