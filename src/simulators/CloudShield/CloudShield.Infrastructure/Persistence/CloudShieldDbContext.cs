using CloudShield.Application.Abstractions;
using CloudShield.Domain.Entitlements;
using CloudShield.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CloudShield.Infrastructure.Persistence;

public sealed class CloudShieldDbContext : DbContext, ICloudShieldUnitOfWork
{
    public CloudShieldDbContext(DbContextOptions<CloudShieldDbContext> options)
        : base(options)
    {
    }

    public DbSet<CloudShieldEntitlement> Entitlements => Set<CloudShieldEntitlement>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CloudShieldDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
