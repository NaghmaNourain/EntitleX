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

    private static EntitleXDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EntitleXDbContext(options);
    }
}
