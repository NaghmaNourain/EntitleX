using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NetworkGuard.Application.Abstractions;
using NetworkGuard.Domain.Licenses;
using NetworkGuard.Domain.Operations;

namespace NetworkGuard.Infrastructure.Persistence;

public sealed class NetworkGuardDbContext : DbContext, INetworkGuardUnitOfWork
{
    public NetworkGuardDbContext(DbContextOptions<NetworkGuardDbContext> options)
        : base(options)
    {
    }

    public DbSet<NetworkGuardLicense> Licenses => Set<NetworkGuardLicense>();

    public DbSet<AppliedLicenseOperation> AppliedLicenseOperations => Set<AppliedLicenseOperation>();

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            await action(cancellationToken);
            return;
        }

        await using IDbContextTransaction transaction =
            await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NetworkGuardDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
