using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetworkGuard.Domain.Licenses;
using NetworkGuard.Infrastructure.Persistence;

namespace NetworkGuard.Infrastructure.Seeding;

public static class NetworkGuardDemoDataSeeder
{
    private static readonly Guid Lic1Id = Guid.Parse("22222222-2222-4222-8222-222222222201");
    private static readonly Guid Lic2Id = Guid.Parse("22222222-2222-4222-8222-222222222202");
    private static readonly Guid Lic3Id = Guid.Parse("22222222-2222-4222-8222-222222222203");

    private static readonly DateTimeOffset SeedEpoch =
        DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NetworkGuardDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("NetworkGuard.DemoDataSeeder");

        var seeds = CreateSeeds();
        var inserted = 0;

        foreach (var seed in seeds)
        {
            var exists = await db.Licenses
                .AnyAsync(e => e.LicenseId == seed.LicenseId, cancellationToken);

            if (exists)
            {
                continue;
            }

            db.Licenses.Add(seed);
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} NetworkGuard demo licenses.", inserted);
        }
        else
        {
            logger.LogDebug("NetworkGuard demo licenses already present.");
        }
    }

    internal static IReadOnlyList<NetworkGuardLicense> CreateSeeds() =>
    [
        NetworkGuardLicense.Create(
            licenseId: "NG-LIC-9001",
            tenantExternalId: "CUST-1001",
            skuCode: "NG-FW-ADV",
            licenseGroup: "Firewall",
            displayName: "NetworkGuard Firewall Advanced",
            validUntilUtc: SeedEpoch.AddMonths(8),
            id: Lic1Id,
            createdAtUtc: SeedEpoch),
        NetworkGuardLicense.Create(
            licenseId: "NG-LIC-9002",
            tenantExternalId: "CUST-1001",
            skuCode: "NG-VPN-STD",
            licenseGroup: "Connectivity",
            displayName: "NetworkGuard VPN Standard",
            validUntilUtc: SeedEpoch.AddMonths(4),
            id: Lic2Id,
            createdAtUtc: SeedEpoch),
        NetworkGuardLicense.Create(
            licenseId: "NG-LIC-9100",
            tenantExternalId: "CUST-3003",
            skuCode: "NG-FW-ADV",
            licenseGroup: "Firewall",
            displayName: "NetworkGuard Firewall Advanced (CUST-3003)",
            validUntilUtc: SeedEpoch.AddMonths(12),
            id: Lic3Id,
            createdAtUtc: SeedEpoch)
    ];
}
