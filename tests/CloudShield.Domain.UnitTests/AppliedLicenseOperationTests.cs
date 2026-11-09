using CloudShield.Domain.Common;
using CloudShield.Domain.Entitlements;
using CloudShield.Domain.Operations;
using EntitleX.SharedKernel.Ids;

namespace CloudShield.Domain.UnitTests;

public sealed class AppliedLicenseOperationTests
{
    [Fact]
    public void CreateApplied_RequiresNewExpiryAfterPrevious()
    {
        var previous = DateTimeOffset.Parse("2026-06-01Z");

        Assert.Throws<DomainException>(() =>
            AppliedLicenseOperation.CreateApplied(
                LogicalOperationId.New(),
                AttemptId.New(),
                "CS-ENT-1001",
                previous,
                previous));
    }

    [Fact]
    public void ExtendExpiry_UpdatesEntitlementClock()
    {
        var entitlement = CloudShieldEntitlement.Create(
            "CS-ENT-1001",
            "CUST-1001",
            "CS-SECURE-EDGE",
            "SecuritySuite",
            "Secure Edge",
            DateTimeOffset.Parse("2026-06-01Z"),
            createdAtUtc: DateTimeOffset.Parse("2026-01-01Z"));

        var next = DateTimeOffset.Parse("2026-12-01Z");
        entitlement.ExtendExpiry(next, updatedAtUtc: DateTimeOffset.Parse("2026-03-01Z"));

        Assert.Equal(next, entitlement.ExpiresAtUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-03-01Z"), entitlement.UpdatedAtUtc);
    }

    [Fact]
    public void CreateApplied_PersistsLogicalAndAttemptIds()
    {
        var logical = LogicalOperationId.From(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
        var attempt = AttemptId.From(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
        var previous = DateTimeOffset.Parse("2026-06-01Z");
        var next = DateTimeOffset.Parse("2026-12-01Z");

        var op = AppliedLicenseOperation.CreateApplied(
            logical,
            attempt,
            "CS-ENT-1001",
            previous,
            next,
            createdAtUtc: DateTimeOffset.Parse("2026-03-01Z"));

        Assert.Equal(logical, op.LogicalOperationId);
        Assert.Equal(attempt, op.AttemptId);
        Assert.Equal(AppliedOperationStatus.Applied, op.Status);
        Assert.Equal(previous, op.PreviousExpiryUtc);
        Assert.Equal(next, op.NewExpiryUtc);
    }
}
