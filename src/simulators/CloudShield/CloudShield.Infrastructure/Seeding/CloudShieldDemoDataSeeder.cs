using CloudShield.Domain.Entitlements;
using CloudShield.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CloudShield.Infrastructure.Seeding;

/// <summary>Idempotent, deterministic demo entitlements for local/dev aggregation demos.</summary>
public static class CloudShieldDemoDataSeeder
{
    // Fixed GUIDs keep seeds stable across restarts.
    private static readonly Guid Ent1Id = Guid.Parse("11111111-1111-4111-8111-111111111101");
    private static readonly Guid Ent2Id = Guid.Parse("11111111-1111-4111-8111-111111111102");
    private static readonly Guid Ent3Id = Guid.Parse("11111111-1111-4111-8111-111111111103");

    private static readonly DateTimeOffset SeedEpoch =
        DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CloudShieldDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CloudShield.DemoDataSeeder");

        var seeds = CreateSeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await db.Entitlements
                .AnyAsync(e => e.EntitlementKey == seed.EntitlementKey, cancellationToken);

            if (exists)
            {
                continue;
            }

            db.Entitlements.Add(seed);
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} CloudShield demo entitlements.", inserted);
        }
        else
        {
            logger.LogDebug("CloudShield demo entitlements already present.");
        }
    }

    internal static IReadOnlyList<CloudShieldEntitlement> CreateSeeds() =>
    [
        CloudShieldEntitlement.Create(
            entitlementKey: "CS-ENT-1001",
            customerAccountId: "CUST-1001",
            productCode: "CS-SECURE-EDGE",
            productGroup: "SecuritySuite",
            displayName: "CloudShield Secure Edge",
            expiresAtUtc: SeedEpoch.AddMonths(6),
            id: Ent1Id,
            createdAtUtc: SeedEpoch),
        CloudShieldEntitlement.Create(
            entitlementKey: "CS-ENT-1002",
            customerAccountId: "CUST-1001",
            productCode: "CS-IDENTITY-PLUS",
            productGroup: "Identity",
            displayName: "CloudShield Identity Plus",
            expiresAtUtc: SeedEpoch.AddMonths(9),
            id: Ent2Id,
            createdAtUtc: SeedEpoch),
        CloudShieldEntitlement.Create(
            entitlementKey: "CS-ENT-2001",
            customerAccountId: "CUST-2002",
            productCode: "CS-SECURE-EDGE",
            productGroup: "SecuritySuite",
            displayName: "CloudShield Secure Edge (CUST-2002)",
            expiresAtUtc: SeedEpoch.AddMonths(3),
            id: Ent3Id,
            createdAtUtc: SeedEpoch)
    ];
}
