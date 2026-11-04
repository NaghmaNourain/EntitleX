using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NetworkGuard.Infrastructure.Persistence;

public sealed class NetworkGuardDbContextFactory : IDesignTimeDbContextFactory<NetworkGuardDbContext>
{
    public NetworkGuardDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NetworkGuardDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=networkguard;Username=entitlex;Password=entitlex",
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                    npgsql.MigrationsAssembly(typeof(NetworkGuardDbContext).Assembly.FullName);
                })
            .Options;

        return new NetworkGuardDbContext(options);
    }
}
