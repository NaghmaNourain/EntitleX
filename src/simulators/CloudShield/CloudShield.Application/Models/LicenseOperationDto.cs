namespace CloudShield.Application.Models;

public sealed record LicenseOperationDto(
    Guid LogicalOperationId,
    Guid AttemptId,
    string EntitlementKey,
    string Status,
    DateTimeOffset PreviousExpiryUtc,
    DateTimeOffset NewExpiryUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason,
    bool AlreadyApplied);
