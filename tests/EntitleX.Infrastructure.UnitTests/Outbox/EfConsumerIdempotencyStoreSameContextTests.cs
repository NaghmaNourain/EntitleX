using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.UnitTests.Outbox;

public sealed class EfConsumerIdempotencyStoreSameContextTests
{
    [Fact]
    public async Task Second_begin_on_same_context_returns_false_without_tracking_conflict()
    {
        await using var db = CreateDb();
        var store = new EfConsumerIdempotencyStore(db);

        Assert.True(await store.TryBeginAsync("consumer", "msg-1", "FulfillmentCommand"));
        Assert.False(await store.TryBeginAsync("consumer", "msg-1", "FulfillmentCommand"));
        Assert.Equal(1, await db.ProcessedMessages.CountAsync());
    }

    private static EntitleXDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EntitleXDbContext(options);
    }
}
