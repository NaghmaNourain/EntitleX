using CloudShield.Domain.Entitlements;
using CloudShield.Infrastructure.Persistence;
using CloudShield.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace CloudShield.Infrastructure.IntegrationTests;

public sealed class LicenseExtensionApplierTests : IAsyncLifetime
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
    public async Task ApplyAsync_IsIdempotentOnLogicalOperationId()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.Entitlements.Add(CloudShieldEntitlement.Create(
            "CS-ENT-1001",
            "CUST-1001",
            "CS-SECURE-EDGE",
            "SecuritySuite",
            "Secure Edge",
            DateTimeOffset.Parse("2026-06-01Z"),
            createdAtUtc: DateTimeOffset.Parse("2026-01-01Z")));
        await db.SaveChangesAsync();

        var applier = new LicenseExtensionApplier(db);
        var logical = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var request1 = new SubmitLicenseExtensionRequest(
            logical,
            Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
            DateTimeOffset.Parse("2026-12-01Z"));

        var first = await applier.ApplyAsync("CS-ENT-1001", request1);
        Assert.False(first.AlreadyApplied);
        Assert.Equal("Applied", first.Status);

        var request2 = new SubmitLicenseExtensionRequest(
            logical,
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"),
            DateTimeOffset.Parse("2027-01-01Z"));

        var second = await applier.ApplyAsync("CS-ENT-1001", request2);
        Assert.True(second.AlreadyApplied);
        Assert.Equal(first.NewExpiryUtc, second.NewExpiryUtc);
        Assert.Equal(1, await db.AppliedLicenseOperations.CountAsync());

        var entitlement = await db.Entitlements.SingleAsync(e => e.EntitlementKey == "CS-ENT-1001");
        Assert.Equal(DateTimeOffset.Parse("2026-12-01Z"), entitlement.ExpiresAtUtc);
    }

    private CloudShieldDbContext CreateDbContext()
    {
        Assert.NotNull(_postgres);

        var options = new DbContextOptionsBuilder<CloudShieldDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(CloudShieldDbContext).Assembly.FullName);
            })
            .Options;

        return new CloudShieldDbContext(options);
    }
}
