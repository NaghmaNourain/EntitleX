using EntitleX.Application.Fulfillment;
using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Messaging.Contracts;
using EntitleX.Messaging.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace EntitleX.Infrastructure.IntegrationTests;

public sealed class OutboxPersistenceTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("entitlex_platform")
            .WithUsername("entitlex")
            .WithPassword("entitlex")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task Enqueue_PersistsOutboxInSameTransactionAsRequest()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var requested = baseline.AddMonths(1);

        var aggregate = LicenseExtensionRequest.CreateFresh(
            "EXT-OUTBOX-1",
            "outbox@example.com",
            [
                (
                    EntitlementId.From("cs-ent-outbox"),
                    ProviderSystem.CloudShield,
                    baseline,
                    requested)
            ]);

        aggregate.Submit();
        aggregate.MarkPendingApproval();
        aggregate.RecordApproval("scope-hash");

        var options = Options.Create(new MessagingOptions
        {
            Sqs = { FulfillmentQueueName = "entitlex-fulfillment" },
            Outbox = { MaxAttempts = 5 }
        });

        var writer = new OutboxWriter(db, options);
        var fulfillment = new FulfillmentOutboxService(writer, options);

        db.LicenseExtensionRequests.Add(aggregate);
        await fulfillment.EnqueueAfterFinalApprovalAsync(aggregate);
        await db.SaveChangesAsync();

        var messages = await db.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Single(messages);
        Assert.Equal(MessageTypes.FulfillmentCommand, messages[0].MessageType);
        Assert.Equal(OutboxDestinationKind.Sqs, messages[0].DestinationKind);
        Assert.Equal("entitlex-fulfillment", messages[0].Destination);
        Assert.Equal(OutboxMessageStatus.Pending, messages[0].Status);
        Assert.Equal(aggregate.Id.Value, messages[0].RequestId);
        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment, aggregate.Status);
    }

    [DockerFact]
    public async Task IdempotencyStore_SecondClaim_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var store = new EfConsumerIdempotencyStore(db);
        var first = await store.TryBeginAsync("test-consumer", "msg-1", MessageTypes.FulfillmentCommand);
        var second = await store.TryBeginAsync("test-consumer", "msg-1", MessageTypes.FulfillmentCommand);

        Assert.True(first);
        Assert.False(second);
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.ProcessedMessages.CountAsync());
    }

    [DockerFact]
    public async Task ClaimBatch_UsesSkipLockedSemantics()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var options = Options.Create(new MessagingOptions
        {
            Outbox = { BatchSize = 10, LockDurationSeconds = 30, MaxAttempts = 8 }
        });

        for (var i = 0; i < 3; i++)
        {
            db.OutboxMessages.Add(OutboxMessage.Create(
                Guid.NewGuid(),
                MessageTypes.FulfillmentCommand,
                """{"requestId":"00000000-0000-0000-0000-000000000001"}""",
                OutboxDestinationKind.Sqs,
                "entitlex-fulfillment",
                maxAttempts: 8));
        }

        await db.SaveChangesAsync();

        var claimer = new OutboxClaimer(db, options);
        var claimed = await claimer.ClaimBatchAsync("worker-a");

        Assert.Equal(3, claimed.Count);
        Assert.All(claimed, m => Assert.Equal(OutboxMessageStatus.Processing, m.Status));
        Assert.All(claimed, m => Assert.Equal(1, m.AttemptCount));

        var again = await claimer.ClaimBatchAsync("worker-b");
        Assert.Empty(again);
    }

    private EntitleXDbContext CreateDbContext()
    {
        Assert.NotNull(_container);

        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
            })
            .Options;

        return new EntitleXDbContext(options);
    }
}
