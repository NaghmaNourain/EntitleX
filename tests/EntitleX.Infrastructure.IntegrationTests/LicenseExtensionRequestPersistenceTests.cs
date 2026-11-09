using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EntitleX.Infrastructure.IntegrationTests;

public sealed class LicenseExtensionRequestPersistenceTests : IAsyncLifetime
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
    public async Task PersistAndLoad_RoundTripsAggregateWithItems()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var requested = baseline.AddMonths(6);

        var aggregate = LicenseExtensionRequest.CreateFresh(
            "EXT-INT-001",
            "integration@example.com",
            [
                (
                    EntitlementId.From("cs-ent-1"),
                    ProviderSystem.CloudShield,
                    baseline,
                    requested),
                (
                    EntitlementId.From("ng-ent-9"),
                    ProviderSystem.NetworkGuard,
                    baseline,
                    requested)
            ]);

        aggregate.Submit();

        var repository = new LicenseExtensionRequestRepository(db);
        await repository.AddAsync(aggregate);
        await db.SaveChangesAsync();

        var loaded = await repository.GetByIdAsync(aggregate.Id);

        Assert.NotNull(loaded);
        Assert.Equal(aggregate.Id, loaded.Id);
        Assert.Equal("EXT-INT-001", loaded.ExtensionNumber);
        Assert.Null(loaded.ParentId);
        Assert.Equal(LicenseExtensionRequestStatus.Submitted, loaded.Status);
        Assert.Equal(0, loaded.ApprovalVersion);
        Assert.Null(loaded.ApprovedScopeHash);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Contains(loaded.Items, i => i.EntitlementId.Value == "cs-ent-1");
        Assert.Contains(loaded.Items, i => i.Provider == ProviderSystem.NetworkGuard);
    }

    [DockerFact]
    public async Task ExtensionNumber_IsUnique()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var requested = baseline.AddMonths(3);

        var first = LicenseExtensionRequest.CreateFresh(
            "EXT-DUP",
            "a@example.com",
            [(EntitlementId.From("e1"), ProviderSystem.CloudShield, baseline, requested)]);

        var second = LicenseExtensionRequest.CreateFresh(
            "EXT-DUP",
            "b@example.com",
            [(EntitlementId.From("e2"), ProviderSystem.NetworkGuard, baseline, requested)]);

        db.LicenseExtensionRequests.Add(first);
        await db.SaveChangesAsync();

        db.LicenseExtensionRequests.Add(second);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
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
