using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;

namespace EntitleX.Application.UnitTests.Idempotency;

/// <summary>
/// Application-layer contract checks for write idempotency lookups
/// (API helper EnsureReplayMatches mirrors this matrix).
/// </summary>
public sealed class ApiIdempotencyLookupTests
{
    [Fact]
    public void Lookup_RoundTrips_Fields()
    {
        var now = DateTimeOffset.Parse("2026-11-09T00:00:00Z");
        var lookup = new ApiIdempotencyLookup(
            "key-1",
            ApiIdempotencyOperations.CreateLicenseExtensionRequest,
            "user-1",
            "abc123",
            Guid.NewGuid().ToString("D"),
            now);

        Assert.Equal("key-1", lookup.IdempotencyKey);
        Assert.Equal(ApiIdempotencyOperations.CreateLicenseExtensionRequest, lookup.Operation);
        Assert.Equal("user-1", lookup.ActorUserId);
        Assert.Equal("abc123", lookup.RequestFingerprint);
    }

    [Fact]
    public void Mismatch_IsConflict()
    {
        var existing = new ApiIdempotencyLookup(
            "key-1",
            ApiIdempotencyOperations.StartApproval,
            "user-1",
            "fp-a",
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow);

        Assert.Throws<ConflictException>(() =>
            EnsureMatches(existing, ApiIdempotencyOperations.StartApproval, "user-1", "fp-b"));
        Assert.Throws<ConflictException>(() =>
            EnsureMatches(existing, ApiIdempotencyOperations.DecideApproval, "user-1", "fp-a"));
        Assert.Throws<ConflictException>(() =>
            EnsureMatches(existing, ApiIdempotencyOperations.StartApproval, "user-2", "fp-a"));
    }

    private static void EnsureMatches(
        ApiIdempotencyLookup existing,
        string operation,
        string actorUserId,
        string fingerprint)
    {
        if (!string.Equals(existing.Operation, operation, StringComparison.Ordinal)
            || !string.Equals(existing.ActorUserId, actorUserId, StringComparison.Ordinal)
            || !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"Idempotency key '{existing.IdempotencyKey}' is already bound to a different write.");
        }
    }
}
