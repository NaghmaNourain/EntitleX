using EntitleX.SharedKernel.Ids;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.IntegrationTests;

/// <summary>
/// Dual concurrent claim attempts against real Postgres unique indexes.
/// Skips automatically when Postgres is unavailable.
/// </summary>
public sealed class RetryClaimConcurrencyTests : IAsyncLifetime
{
    private TestPostgresLease? _postgres;

    public async Task InitializeAsync()
    {
        if (!TestPostgres.IsConfigured)
        {
            return;
        }

        _postgres = await TestPostgres.AcquireAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task DualClaims_SameSourceItems_DifferentKeys_OnlyOneSucceeds()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var sourceId = RequestId.New();
        var logical = LogicalOperationId.New();
        var storeA = new EfRetryClaimStore(db);
        var storeB = new EfRetryClaimStore(db);

        var retryA = RequestId.New();
        var retryB = RequestId.New();

        var first = await storeA.TryClaimItemsAsync(
            sourceId, retryA, "key-a", [logical], notes: null);
        var second = await storeB.TryClaimItemsAsync(
            sourceId, retryB, "key-b", [logical], notes: null);

        Assert.True(first);
        Assert.False(second);

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.RetryItemClaims.CountAsync());
        Assert.Equal(1, await db.ApiIdempotencyRecords.CountAsync());
    }

    [DockerFact]
    public async Task SameIdempotencyKey_SecondClaim_ReturnsFalse_AndLookupReturnsPrior()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var sourceId = RequestId.New();
        var retryId = RequestId.New();
        var store = new EfRetryClaimStore(db);

        Assert.True(await store.TryClaimItemsAsync(
            sourceId, retryId, "same-key", [LogicalOperationId.New()], null));
        await db.SaveChangesAsync();

        Assert.False(await store.TryClaimItemsAsync(
            sourceId, RequestId.New(), "same-key", [LogicalOperationId.New()], null));

        var prior = await store.FindByIdempotencyKeyAsync("same-key");
        Assert.NotNull(prior);
        Assert.Equal(retryId, prior!.RetryRequestId);
    }

    private EntitleXDbContext CreateDbContext()
    {
        Assert.NotNull(_postgres);

        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
            })
            .Options;
        return new EntitleXDbContext(options);
    }
}
