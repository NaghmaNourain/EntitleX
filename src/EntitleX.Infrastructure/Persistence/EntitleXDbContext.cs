using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Persistence;

public sealed class EntitleXDbContext : DbContext, IUnitOfWork
{
    public EntitleXDbContext(DbContextOptions<EntitleXDbContext> options)
        : base(options)
    {
    }

    public DbSet<LicenseExtensionRequest> LicenseExtensionRequests => Set<LicenseExtensionRequest>();

    public DbSet<ApprovalCase> ApprovalCases => Set<ApprovalCase>();

    public DbSet<ApprovalStage> ApprovalStages => Set<ApprovalStage>();

    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public DbSet<FulfillmentItemResult> FulfillmentItemResults => Set<FulfillmentItemResult>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<LicenseExtensionRequest>())
        {
            if (entry.State is EntityState.Modified or EntityState.Added)
            {
                var rowVersion = entry.Property(e => e.RowVersion);
                if (entry.State == EntityState.Added)
                {
                    rowVersion.CurrentValue = 0;
                }
                else
                {
                    rowVersion.OriginalValue = entry.Entity.RowVersion;
                    rowVersion.CurrentValue = unchecked(entry.Entity.RowVersion + 1);
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EntitleXDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
