using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using EntitleX.Infrastructure.Audit;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Persistence;

public sealed class EntitleXDbContext : DbContext, IUnitOfWork
{
    public EntitleXDbContext(DbContextOptions<EntitleXDbContext> options)
        : base(options)
    {
    }

    public DbSet<LicenseExtensionRequest> LicenseExtensionRequests => Set<LicenseExtensionRequest>();

    public DbSet<RetryHistoryEntry> RetryHistoryEntries => Set<RetryHistoryEntry>();

    public DbSet<ApprovalCase> ApprovalCases => Set<ApprovalCase>();

    public DbSet<ApprovalStage> ApprovalStages => Set<ApprovalStage>();

    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    public DbSet<LicenseExtensionSaga> LicenseExtensionSagas => Set<LicenseExtensionSaga>();

    public DbSet<SagaTransition> SagaTransitions => Set<SagaTransition>();

    public DbSet<SagaItemOutcome> SagaItemOutcomes => Set<SagaItemOutcome>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public DbSet<FulfillmentItemResult> FulfillmentItemResults => Set<FulfillmentItemResult>();

    public DbSet<ApiIdempotencyRecord> ApiIdempotencyRecords => Set<ApiIdempotencyRecord>();

    public DbSet<RetryItemClaim> RetryItemClaims => Set<RetryItemClaim>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

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
