using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CloudShield.Infrastructure.Persistence;

public sealed class CloudShieldDbContextFactory : IDesignTimeDbContextFactory<CloudShieldDbContext>
{
    public CloudShieldDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CloudShieldDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=cloudshield;Username=entitlex;Password=entitlex",
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                    npgsql.MigrationsAssembly(typeof(CloudShieldDbContext).Assembly.FullName);
                })
            .Options;

        return new CloudShieldDbContext(options);
    }
}
