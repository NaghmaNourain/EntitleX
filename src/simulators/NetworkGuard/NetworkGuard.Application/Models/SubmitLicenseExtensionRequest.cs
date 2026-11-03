namespace NetworkGuard.Application.Models;

public sealed record SubmitLicenseExtensionRequest(
    Guid LogicalOperationId,
    Guid AttemptId,
    DateTimeOffset NewExpiryUtc);
