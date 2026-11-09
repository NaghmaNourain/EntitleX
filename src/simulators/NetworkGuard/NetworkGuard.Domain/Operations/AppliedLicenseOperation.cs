using EntitleX.SharedKernel.Ids;
using NetworkGuard.Domain.Common;

namespace NetworkGuard.Domain.Operations;

public sealed class AppliedLicenseOperation
{
    private AppliedLicenseOperation()
    {
        LicenseId = null!;
    }

    private AppliedLicenseOperation(
        Guid id,
        LogicalOperationId logicalOperationId,
        AttemptId attemptId,
        string licenseId,
        DateTimeOffset previousExpiryUtc,
        DateTimeOffset newExpiryUtc,
        AppliedOperationStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? completedAtUtc,
        string? failureReason)
    {
        Id = id;
        LogicalOperationId = logicalOperationId;
        AttemptId = attemptId;
        LicenseId = licenseId;
        PreviousExpiryUtc = previousExpiryUtc;
        NewExpiryUtc = newExpiryUtc;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        CompletedAtUtc = completedAtUtc;
        FailureReason = failureReason;
    }

    public Guid Id { get; private set; }

    public LogicalOperationId LogicalOperationId { get; private set; }

    public AttemptId AttemptId { get; private set; }

    public string LicenseId { get; private set; }

    public DateTimeOffset PreviousExpiryUtc { get; private set; }

    public DateTimeOffset NewExpiryUtc { get; private set; }

    public AppliedOperationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public static AppliedLicenseOperation CreateApplied(
        LogicalOperationId logicalOperationId,
        AttemptId attemptId,
        string licenseId,
        DateTimeOffset previousExpiryUtc,
        DateTimeOffset newExpiryUtc,
        DateTimeOffset? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(licenseId))
        {
            throw new DomainException("LicenseId is required.");
        }

        if (newExpiryUtc <= previousExpiryUtc)
        {
            throw new DomainException("NewExpiryUtc must be after PreviousExpiryUtc.");
        }

        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        return new AppliedLicenseOperation(
            Guid.NewGuid(),
            logicalOperationId,
            attemptId,
            licenseId.Trim(),
            previousExpiryUtc,
            newExpiryUtc,
            AppliedOperationStatus.Applied,
            now,
            now,
            failureReason: null);
    }
}
