using EntitleX.Domain.LicenseExtension;
using EntitleX.SharedKernel.Ids;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.UnitTests.Retry;

public sealed class EfRetryClaimStoreSameContextTests
{
    [Fact]
    public async Task Second_claim_on_same_context_returns_false_without_tracking_conflict()
    {
        await using var db = CreateDb();
        var store = new EfRetryClaimStore(db);
        var sourceId = RequestId.New();
        var logical = LogicalOperationId.New();

        Assert.True(await store.TryClaimItemsAsync(
            sourceId, RequestId.New(), "key-a", [logical], notes: null));

        // Same DbContext, unsaved pending claim — must not throw on Add().
        Assert.False(await store.TryClaimItemsAsync(
            sourceId, RequestId.New(), "key-b", [logical], notes: null));

        await db.SaveChangesAsync();
        Assert.Equal(1, await db.RetryItemClaims.CountAsync());
    }

    [Fact]
    public async Task Reclaim_after_terminal_failed_child_same_context_succeeds()
    {
        await using var db = CreateDb();
        var store = new EfRetryClaimStore(db);
        var sourceId = RequestId.New();
        var logical = LogicalOperationId.New();
        var firstRetry = await SeedFailedRequestAsync(db);

        Assert.True(await store.TryClaimItemsAsync(
            sourceId, firstRetry, "key-1", [logical], notes: null));
        await db.SaveChangesAsync();

        var secondRetry = RequestId.New();
        Assert.True(await store.TryClaimItemsAsync(
            sourceId, secondRetry, "key-2", [logical], notes: null));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.RetryItemClaims.CountAsync());
        Assert.Equal(secondRetry, (await db.RetryItemClaims.SingleAsync()).RetryRequestId);
    }

    private static async Task<RequestId> SeedFailedRequestAsync(EntitleXDbContext db)
    {
        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var request = LicenseExtensionRequest.CreateFresh(
            $"EXT-{Guid.NewGuid():N}"[..20],
            "rep@entitlex.local",
            [(EntitlementId.From("ent-x"), ProviderSystem.CloudShield, baseline, baseline.AddDays(5))]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();
        request.CompleteFulfillment(0, 1);
        db.LicenseExtensionRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static EntitleXDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EntitleXDbContext(options);
    }
}
