using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntitleX.Infrastructure.Persistence;

/// <summary>Design-time factory for EF Core migrations targeting entitlex_platform.</summary>
public sealed class EntitleXDbContextFactory : IDesignTimeDbContextFactory<EntitleXDbContext>
{
    public EntitleXDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ENTITLEX_PLATFORM_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=entitlex_platform;Username=entitlex;Password=entitlex";

        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
                npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
            })
            .Options;

        return new EntitleXDbContext(options);
    }
}
