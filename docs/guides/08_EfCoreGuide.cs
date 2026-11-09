// =============================================================================
// EntitleX Educational Guide 08 — Entity Framework Core Guide
// =============================================================================
//
// HOW TO USE THIS FILE
// --------------------
// Standalone teaching guide. Samples LOOK like real EF configuration but use
// inline stubs so you can read them without compiling against Npgsql packages.
// Pair with docs/database-schema.md and the real configurations under
// src/EntitleX.Infrastructure/Persistence/.
//
// REAL REPO ANCHORS
// -----------------
//   src/EntitleX.Infrastructure/Persistence/EntitleXDbContext.cs
//   src/EntitleX.Infrastructure/Persistence/EntitleXDbContextFactory.cs
//   src/EntitleX.Infrastructure/Persistence/Repositories/LicenseExtensionRequestRepository.cs
//   src/EntitleX.Infrastructure/Persistence/Repositories/LicenseExtensionSagaRepository.cs
//   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
//   src/EntitleX.Infrastructure/Outbox/OutboxMessage.cs
//   src/EntitleX.Infrastructure/Outbox/ProcessedMessage.cs
//   src/EntitleX.Infrastructure/Retry/ApiIdempotencyRecord.cs
//   src/EntitleX.Infrastructure/Retry/RetryItemClaim.cs
//   src/EntitleX.Infrastructure/DependencyInjection.cs
//   src/simulators/CloudShield/.../CloudShieldDbContext*
//   src/simulators/NetworkGuard/.../NetworkGuardDbContext*
//   docs/database-schema.md
//   docs/performance.md
//
// TABLE OF CONTENTS
// -----------------
//   PART 0  — Inline domain + EF-shaped stubs
//   PART 1  — Three databases overview
//   PART 2  — EntitleXDbContext as IUnitOfWork
//   PART 3  — DbSets catalog
//   PART 4  — Owned collections & aggregate mapping
//   PART 5  — row_version concurrency token
//   PART 6  — AsNoTracking reads
//   PART 7  — Keyset pagination (pending approvals)
//   PART 8  — Bulk attach ≤1000 with AutoDetectChangesEnabled=false
//   PART 9  — MaxBatchSize(100) + Npgsql tuning
//   PART 10 — Raw SQL FOR UPDATE SKIP LOCKED outbox claim
//   PART 11 — Migrations notes (design-time factory)
//   PART 12 — Idempotency / retry / processed tables
//   PART 13 — Saga + approval mapping sketches
//   PART 14 — Simulator DBs (cloudshield / networkguard)
//   PART 15 — Interview notes + anti-patterns
//
// =============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EntitleX.Guides.EfCore
{

#region PART 0 — Inline domain + EF-shaped teaching stubs

namespace Domain
{
    public enum ProviderSystem { CloudShield = 0, NetworkGuard = 1 }
    public enum ApprovalBand { AutoApprove, RenewalManagerOnly, RenewalManagerThenDealDesk, Unsupported }
    public enum SagaState
    {
        Requested, PendingRenewalManagerApproval, PendingDealDeskApproval, Approved,
        QueuedForFulfillment, Processing, PartiallyCompleted, Completed, Failed,
        ReconciliationRequired, Escalated, Rejected
    }

    public sealed record RequestId(Guid Value);
    public sealed record LogicalOperationId(Guid Value);
    public sealed record AttemptId(Guid Value);

    public sealed class LicenseExtensionRequestItem
    {
        public Guid Id { get; set; }
        public LogicalOperationId LogicalOperationId { get; set; } = new(Guid.NewGuid());
        public AttemptId AttemptId { get; set; } = new(Guid.NewGuid());
        public AttemptId? ParentId { get; set; }
        public string EntitlementId { get; set; } = "";
        public ProviderSystem Provider { get; set; }
        public DateTimeOffset CurrentExpiryUtc { get; set; }
        public DateTimeOffset RequestedExpiryUtc { get; set; }
    }

    public sealed class LicenseExtensionRequest
    {
        public RequestId Id { get; set; } = new(Guid.NewGuid());
        public string CreatedByUserId { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Status { get; set; } = "Requested";
        /// <summary>Optimistic concurrency token (column row_version).</summary>
        public long RowVersion { get; set; }
        public List<LicenseExtensionRequestItem> Items { get; set; } = new();
    }

    public sealed class ApprovalCase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public RequestId RequestId { get; set; } = new(Guid.Empty);
        public ApprovalBand Band { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<ApprovalStage> Stages { get; set; } = new();
    }

    public sealed class ApprovalStage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Role { get; set; } = ""; // RenewalManager | DealDeskManager
        public string Status { get; set; } = "Pending";
        public int Ordinal { get; set; }
    }

    public sealed class LicenseExtensionSaga
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public RequestId RequestId { get; set; } = new(Guid.Empty);
        public SagaState State { get; set; } = SagaState.Requested;
        public bool RequiresDealDesk { get; set; }
        public List<SagaItemOutcome> ItemOutcomes { get; set; } = new();
        public List<SagaTransition> Transitions { get; set; } = new();
    }

    public sealed class SagaItemOutcome
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public LogicalOperationId LogicalOperationId { get; set; } = new(Guid.NewGuid());
        public string Status { get; set; } = "Pending"; // Pending|Succeeded|Failed
    }

    public sealed class SagaTransition
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public SagaState FromState { get; set; }
        public SagaState ToState { get; set; }
        public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Reason { get; set; } = "";
    }

    public sealed class OutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string MessageType { get; set; } = "";
        public string PayloadJson { get; set; } = "{}";
        public string Status { get; set; } = "Pending"; // Pending|Processing|Published|Dead
        public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LockedUntilUtc { get; set; }
        public string? LockedBy { get; set; }
        public int AttemptCount { get; set; }
    }

    public sealed class ProcessedMessage
    {
        public string IdempotencyKey { get; set; } = "";
        public string MessageType { get; set; } = "";
        public DateTimeOffset ProcessedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class ApiIdempotencyRecord
    {
        public string IdempotencyKey { get; set; } = "";
        public Guid RequestId { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class RetryItemClaim
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public LogicalOperationId LogicalOperationId { get; set; } = new(Guid.NewGuid());
        public Guid ClaimedByRequestId { get; set; }
        public DateTimeOffset ClaimedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}

/// <summary>Teaching stand-in for Microsoft.EntityFrameworkCore.DbContext.</summary>
public abstract class FakeDbContext
{
    public FakeChangeTracker ChangeTracker { get; } = new();
    public FakeDatabaseFacade Database { get; } = new();
    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeTracker.Entries.Count);
}

public sealed class FakeChangeTracker
{
    public bool AutoDetectChangesEnabled { get; set; } = true;
    public List<object> Entries { get; } = new();
    public void Clear() => Entries.Clear();
}

public sealed class FakeDatabaseFacade
{
    public Task MigrateAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<FakeTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        Task.FromResult(new FakeTransaction());
}

public sealed class FakeTransaction : IAsyncDisposable
{
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IEntityTypeConfiguration<T>
{
    void Configure(EntityTypeBuilder<T> builder);
}

public sealed class EntityTypeBuilder<T>
{
    private readonly List<string> _notes = new();
    public EntityTypeBuilder<T> ToTable(string name) { _notes.Add($"ToTable({name})"); return this; }
    public EntityTypeBuilder<T> HasKey(string prop) { _notes.Add($"HasKey({prop})"); return this; }
    public EntityTypeBuilder<T> Property(string prop) { _notes.Add($"Property({prop})"); return this; }
    public EntityTypeBuilder<T> IsConcurrencyToken() { _notes.Add("IsConcurrencyToken()"); return this; }
    public EntityTypeBuilder<T> HasColumnName(string name) { _notes.Add($"HasColumnName({name})"); return this; }
    public EntityTypeBuilder<T> OwnsMany(string nav) { _notes.Add($"OwnsMany({nav})"); return this; }
    public EntityTypeBuilder<T> HasIndex(string fields) { _notes.Add($"HasIndex({fields})"); return this; }
    public EntityTypeBuilder<T> IsUnique() { _notes.Add("IsUnique()"); return this; }
    public IReadOnlyList<string> Notes => _notes;
}

#endregion

public static class DemoHost
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX EF Core Guide ===\n");
        Part1_ThreeDatabases.Run();
        Part2_DbContextUnitOfWork.Run();
        Part3_DbSets.Run();
        Part4_OwnedCollections.Run();
        await Part5_RowVersion.RunAsync();
        Part6_AsNoTracking.Run();
        Part7_KeysetPagination.Run();
        await Part8_BulkAttach.RunAsync();
        Part9_MaxBatchSize.Run();
        await Part10_OutboxSkipLocked.RunAsync();
        Part11_Migrations.Run();
        Part12_IdempotencyTables.Run();
        Part13_SagaApprovalMapping.Run();
        Part14_SimulatorDatabases.Run();
        Part15_Interview.Print();
    }
}

// =============================================================================
// PART 1 — Three databases
// =============================================================================
/// <summary>
/// From README / docs/database-schema.md / docs/architecture.md:
///   entitlex_platform → EntitleXDbContext (control plane)
///   cloudshield       → CloudShieldDbContext (simulator)
///   networkguard      → NetworkGuardDbContext (simulator)
/// </summary>
public static class Part1_ThreeDatabases
{
    public static void Run()
    {
        Console.WriteLine("-- PART 1: Three databases --");
        var rows = new[]
        {
            ("entitlex_platform", "EntitleXDbContext", "API, workers, lambdas — requests/sagas/outbox/approvals"),
            ("cloudshield", "CloudShieldDbContext", "CloudShield simulator entitlements + applied ops"),
            ("networkguard", "NetworkGuardDbContext", "NetworkGuard simulator licenses + applied ops"),
        };
        foreach (var (db, ctx, role) in rows)
            Console.WriteLine($"  {db,-18} {ctx,-24} {role}");

        Console.WriteLine("  ConnectionStrings:EntitleXPlatform → platform");
        Console.WriteLine("  Design-time default: Host=localhost;Database=entitlex_platform;Username=entitlex;Password=entitlex");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 2 — EntitleXDbContext as IUnitOfWork
// =============================================================================
/// <summary>
/// Real type: src/EntitleX.Infrastructure/Persistence/EntitleXDbContext.cs
/// Registered: services.AddScoped&lt;IUnitOfWork&gt;(sp =&gt; sp.GetRequiredService&lt;EntitleXDbContext&gt;());
/// </summary>
public sealed class EntitleXDbContext : FakeDbContext, IUnitOfWork
{
    public List<Domain.LicenseExtensionRequest> LicenseExtensionRequests { get; } = new();
    public List<Domain.ApprovalCase> ApprovalCases { get; } = new();
    public List<Domain.ApprovalStage> ApprovalStages { get; } = new();
    public List<Domain.LicenseExtensionSaga> LicenseExtensionSagas { get; } = new();
    public List<Domain.SagaTransition> SagaTransitions { get; } = new();
    public List<Domain.SagaItemOutcome> SagaItemOutcomes { get; } = new();
    public List<Domain.OutboxMessage> OutboxMessages { get; } = new();
    public List<Domain.ProcessedMessage> ProcessedMessages { get; } = new();
    public List<Domain.ApiIdempotencyRecord> ApiIdempotencyRecords { get; } = new();
    public List<Domain.RetryItemClaim> RetryItemClaims { get; } = new();

    /// <summary>
    /// Mirrors real SaveChangesAsync row_version bump for LicenseExtensionRequest.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in LicenseExtensionRequests)
        {
            // Teaching model: treat all tracked requests as modified when saving.
            // Real EF uses ChangeTracker.Entries&lt;LicenseExtensionRequest&gt;().
            entry.RowVersion = unchecked(entry.RowVersion + 1);
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

public static class Part2_DbContextUnitOfWork
{
    public static void Run()
    {
        Console.WriteLine("-- PART 2: EntitleXDbContext as IUnitOfWork --");
        Console.WriteLine("""
  public sealed class EntitleXDbContext : DbContext, IUnitOfWork
  {
      // DbSets…
      public override async Task<int> SaveChangesAsync(...)
      {
          // bump row_version for LicenseExtensionRequest Added/Modified
          return await base.SaveChangesAsync(...);
      }
      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
          modelBuilder.HasDefaultSchema("public");
          modelBuilder.ApplyConfigurationsFromAssembly(typeof(EntitleXDbContext).Assembly);
      }
  }

  Handlers depend on IUnitOfWork — not the concrete context — for testability,
  while repositories take EntitleXDbContext for DbSet access.
""");
        IUnitOfWork uow = new EntitleXDbContext();
        Console.WriteLine($"  IUnitOfWork runtime type={uow.GetType().Name}");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 3 — DbSets catalog
// =============================================================================
public static class Part3_DbSets
{
    public static void Run()
    {
        Console.WriteLine("-- PART 3: DbSets catalog --");
        var sets = new[]
        {
            "LicenseExtensionRequests",
            "RetryHistoryEntries",
            "ApprovalCases",
            "ApprovalStages",
            "ApprovalDecisions",
            "LicenseExtensionSagas",
            "SagaTransitions",
            "SagaItemOutcomes",
            "OutboxMessages",
            "ProcessedMessages",
            "FulfillmentItemResults",
            "ApiIdempotencyRecords",
            "RetryItemClaims",
            "AuditEvents",
            "ApprovalReminderRecords",
            "ExpirationNotificationRecords",
        };
        foreach (var s in sets) Console.WriteLine($"  DbSet<> {s}");
        Console.WriteLine("  Schema: public (HasDefaultSchema)");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 4 — Owned collections & aggregate mapping
// =============================================================================
/// <summary>
/// Items on LicenseExtensionRequest are part of the aggregate — typically mapped
/// as owned/related collections with cascade. Teaching sketch below.
/// </summary>
public sealed class LicenseExtensionRequestConfiguration : IEntityTypeConfiguration<Domain.LicenseExtensionRequest>
{
    public void Configure(EntityTypeBuilder<Domain.LicenseExtensionRequest> builder)
    {
        builder.ToTable("license_extension_requests");
        builder.HasKey(nameof(Domain.LicenseExtensionRequest.Id));
        builder.Property(nameof(Domain.LicenseExtensionRequest.RowVersion))
               .HasColumnName("row_version")
               .IsConcurrencyToken();
        builder.OwnsMany(nameof(Domain.LicenseExtensionRequest.Items));
        builder.HasIndex("CreatedAtUtc");
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<Domain.OutboxMessage>
{
    public void Configure(EntityTypeBuilder<Domain.OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(nameof(Domain.OutboxMessage.Id));
        // Claim hot path: FOR UPDATE SKIP LOCKED on (status, available_at_utc).
        builder.HasIndex("Status, AvailableAtUtc");
        builder.Property(nameof(Domain.OutboxMessage.Status));
        builder.Property(nameof(Domain.OutboxMessage.LockedBy));
    }
}

public static class Part4_OwnedCollections
{
    public static void Run()
    {
        Console.WriteLine("-- PART 4: Owned collections & configurations --");
        var reqCfg = new LicenseExtensionRequestConfiguration();
        var b1 = new EntityTypeBuilder<Domain.LicenseExtensionRequest>();
        reqCfg.Configure(b1);
        foreach (var n in b1.Notes) Console.WriteLine("  Request: " + n);

        var outCfg = new OutboxMessageConfiguration();
        var b2 = new EntityTypeBuilder<Domain.OutboxMessage>();
        outCfg.Configure(b2);
        foreach (var n in b2.Notes) Console.WriteLine("  Outbox: " + n);

        Console.WriteLine("  ApplyConfigurationsFromAssembly loads all IEntityTypeConfiguration<> in Infrastructure.");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 5 — row_version concurrency token
// =============================================================================
public static class Part5_RowVersion
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 5: row_version concurrency token --");
        var db = new EntitleXDbContext();
        var req = new Domain.LicenseExtensionRequest { RowVersion = 0, Status = "Requested" };
        db.LicenseExtensionRequests.Add(req);
        await db.SaveChangesAsync(); // → 1
        Console.WriteLine($"  after insert RowVersion={req.RowVersion}");

        // Simulate two workers reading same version
        long workerASeen = req.RowVersion;
        long workerBSeen = req.RowVersion;
        req.Status = "InFulfillment";
        await db.SaveChangesAsync(); // → 2
        Console.WriteLine($"  workerA expected={workerASeen} actual={req.RowVersion} → {(workerASeen + 1 == req.RowVersion ? "OK" : "CONFLICT")}");

        // Worker B still holds stale expected version
        bool conflict = workerBSeen != req.RowVersion;
        Console.WriteLine($"  workerB stale detect conflict={conflict} (DbUpdateConcurrencyException in real EF)");
        Console.WriteLine("  Column mapping: .HasColumnName(\"row_version\").IsConcurrencyToken()");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 6 — AsNoTracking reads
// =============================================================================
public static class Part6_AsNoTracking
{
    public static void Run()
    {
        Console.WriteLine("-- PART 6: AsNoTracking reads --");
        Console.WriteLine("""
  Use AsNoTracking for:
    - GetLicenseExtensionRequest queries
    - Pending approval lists
    - Saga read models
    - ApiIdempotencyRecords existence checks
    - Audit keyset scans
    - BacklogMetricsProbe outbox counts

  Example (conceptual):
    await _db.LicenseExtensionRequests.AsNoTracking()
        .Where(r => r.Id == id)
        .Select(r => new Dto(...))
        .FirstOrDefaultAsync();

  Tracking is REQUIRED when you mutate aggregates then SaveChangesAsync
  (decide approval, record saga outcomes, claim outbox rows after reload).
""");
        var db = new EntitleXDbContext();
        db.LicenseExtensionRequests.Add(new Domain.LicenseExtensionRequest { Status = "Requested" });
        // Toy "no-tracking projection"
        var readModel = db.LicenseExtensionRequests.Select(r => new { r.Status, r.RowVersion }).ToList();
        Console.WriteLine($"  projected rows={readModel.Count} (imagine AsNoTracking)");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 7 — Keyset pagination
// =============================================================================
/// <summary>
/// PendingApprovalEndpoints accepts afterCreatedAtUtc + afterApprovalCaseId.
/// Prefer keyset over OFFSET for stable deep pages.
/// </summary>
public static class Part7_KeysetPagination
{
    public static void Run()
    {
        Console.WriteLine("-- PART 7: Keyset pagination (pending approvals) --");
        var cases = Enumerable.Range(0, 20)
            .Select(i => new Domain.ApprovalCase
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"),
                CreatedAtUtc = DateTimeOffset.Parse("2026-11-01T00:00:00Z").AddMinutes(i),
                Band = Domain.ApprovalBand.RenewalManagerOnly,
                Status = "Pending",
                Stages =
                {
                    new Domain.ApprovalStage { Role = "RenewalManager", Ordinal = 1, Status = "Pending" }
                }
            })
            .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .ToList();

        // First page
        int take = 5;
        var page1 = cases.Take(take).ToList();
        var cursor = page1[^1];
        Console.WriteLine($"  page1 last cursor CreatedAt={cursor.CreatedAtUtc:o} Id={cursor.Id}");

        // Next page: (CreatedAtUtc > cursor) OR (CreatedAtUtc == cursor AND Id > cursor.Id)
        var page2 = cases
            .Where(c => c.CreatedAtUtc > cursor.CreatedAtUtc ||
                        (c.CreatedAtUtc == cursor.CreatedAtUtc && c.Id.CompareTo(cursor.Id) > 0))
            .Take(take)
            .ToList();
        Console.WriteLine($"  page2 count={page2.Count} first={page2[0].Id}");

        Console.WriteLine("""
  SQL shape:
    WHERE (created_at_utc > @afterCreatedAtUtc)
       OR (created_at_utc = @afterCreatedAtUtc AND id > @afterApprovalCaseId)
    ORDER BY created_at_utc, id
    LIMIT @take
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 8 — Bulk attach ≤1000 items
// =============================================================================
/// <summary>
/// From LicenseExtensionRequestRepository: disable AutoDetectChanges during
/// mass attach of up to 1000 items, restore afterward.
/// </summary>
public static class Part8_BulkAttach
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 8: Bulk attach (AutoDetectChangesEnabled=false) --");
        var db = new EntitleXDbContext();
        var request = new Domain.LicenseExtensionRequest();
        for (int i = 0; i < 1000; i++)
        {
            request.Items.Add(new Domain.LicenseExtensionRequestItem
            {
                EntitlementId = $"ENT-{i:D4}",
                Provider = i % 2 == 0 ? Domain.ProviderSystem.CloudShield : Domain.ProviderSystem.NetworkGuard,
                CurrentExpiryUtc = DateTimeOffset.UtcNow,
                RequestedExpiryUtc = DateTimeOffset.UtcNow.AddDays(30),
            });
        }

        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.LicenseExtensionRequests.Add(request);
            db.ChangeTracker.Entries.Add(request); // toy tracking
            foreach (var item in request.Items) db.ChangeTracker.Entries.Add(item);
            Console.WriteLine($"  attached items={request.Items.Count} with AutoDetectChanges=false");
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        await db.SaveChangesAsync();
        Console.WriteLine("  Persist uses Npgsql MaxBatchSize(100) → ~10 round-trips for 1000 inserts.");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 9 — MaxBatchSize(100)
// =============================================================================
public static class Part9_MaxBatchSize
{
    public static void Run()
    {
        Console.WriteLine("-- PART 9: MaxBatchSize(100) + Npgsql tuning --");
        Console.WriteLine("""
  services.AddDbContext<EntitleXDbContext>(options =>
  {
      options.UseNpgsql(connectionString, npgsql =>
      {
          npgsql.MigrationsHistoryTable("__ef_migrations_history", "public");
          npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
          npgsql.MaxBatchSize(100);   // batch up to 100 commands / round-trip
          npgsql.CommandTimeout(60);
      });
  });

  Connection pool: Npgsql defaults Max Pool Size=100 — tune via connection string
  (Minimum Pool Size / Maximum Pool Size / Timeout). See docs/performance.md.

  Same MaxBatchSize(100) applied in:
    AddInfrastructure
    AddInfrastructureForWorkers
    AddInfrastructureForLambdas
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 10 — FOR UPDATE SKIP LOCKED
// =============================================================================
/// <summary>
/// Teaching recreation of OutboxClaimer SQL.
/// Path: src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
/// </summary>
public static class Part10_OutboxSkipLocked
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 10: Raw SQL FOR UPDATE SKIP LOCKED --");
        Console.WriteLine("""
  WITH claimed AS (
      SELECT id
      FROM outbox_messages
      WHERE (status = 'Pending' AND available_at_utc <= @now)
         OR (status = 'Processing' AND locked_until_utc IS NOT NULL AND locked_until_utc < @now)
      ORDER BY available_at_utc
      LIMIT @batch
      FOR UPDATE SKIP LOCKED
  )
  UPDATE outbox_messages AS o
  SET status = 'Processing',
      attempt_count = o.attempt_count + 1,
      locked_until_utc = @lockedUntil,
      locked_by = @workerId
  FROM claimed
  WHERE o.id = claimed.id
  RETURNING o.id;
""");

        var db = new EntitleXDbContext();
        db.OutboxMessages.Add(new Domain.OutboxMessage { MessageType = "FulfillmentCommandV1", Status = "Pending" });
        db.OutboxMessages.Add(new Domain.OutboxMessage { MessageType = "FulfillmentCommandV1", Status = "Pending" });
        db.OutboxMessages.Add(new Domain.OutboxMessage
        {
            MessageType = "ProviderResultV1",
            Status = "Processing",
            LockedUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-5), // stale
            LockedBy = "dead-worker"
        });

        // In-memory simulation of claim
        var now = DateTimeOffset.UtcNow;
        var claimed = db.OutboxMessages
            .Where(m =>
                (m.Status == "Pending" && m.AvailableAtUtc <= now) ||
                (m.Status == "Processing" && m.LockedUntilUtc < now))
            .OrderBy(m => m.AvailableAtUtc)
            .Take(20)
            .ToList();
        foreach (var m in claimed)
        {
            m.Status = "Processing";
            m.LockedBy = "outbox-publisher-1";
            m.LockedUntilUtc = now.AddSeconds(30);
            m.AttemptCount++;
        }
        Console.WriteLine($"  claimed={claimed.Count} (Pending + stale Processing)");
        Console.WriteLine("  After SQL claim: ChangeTracker.Clear() then reload by ids.");
        await Task.CompletedTask;
        Console.WriteLine();
    }
}

// =============================================================================
// PART 11 — Migrations
// =============================================================================
public static class Part11_Migrations
{
    public static void Run()
    {
        Console.WriteLine("-- PART 11: Migrations notes --");
        Console.WriteLine("""
  Design-time factory:
    src/EntitleX.Infrastructure/Persistence/EntitleXDbContextFactory.cs
    targets Database=entitlex_platform

  History table: public.__ef_migrations_history

  Notable migrations (names from repo):
    InitialLicenseExtension
    ApprovalWorkflow
    OutboxMessaging
    SagaOrchestration
    SelectiveRetryIdempotency
    ReconciliationAudit
    ScheduledNotifications
    PerformanceObservabilityIndexes

  API opt-in: Database:MigrateOnStartup=true → db.Database.MigrateAsync()
  Prefer migrate jobs in real prod; flag is handy for local/docker demos.

  Simulator DBs have their OWN contexts + factories:
    CloudShieldDbContextFactory  → Database=cloudshield
    NetworkGuardDbContextFactory → Database=networkguard
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 12 — Idempotency / retry tables
// =============================================================================
public static class Part12_IdempotencyTables
{
    public static void Run()
    {
        Console.WriteLine("-- PART 12: Idempotency / retry / processed --");
        var db = new EntitleXDbContext();

        // API idempotency (create/retry)
        db.ApiIdempotencyRecords.Add(new Domain.ApiIdempotencyRecord
        {
            IdempotencyKey = "idem-create-001",
            RequestId = Guid.NewGuid()
        });
        Console.WriteLine("  ApiIdempotencyRecords: unique IdempotencyKey → RequestId");

        // Consumer idempotency
        db.ProcessedMessages.Add(new Domain.ProcessedMessage
        {
            IdempotencyKey = "kafka:provider-result:msg-9",
            MessageType = "ProviderLicenseExtensionResultV1"
        });
        Console.WriteLine("  ProcessedMessages: consumer dedupe key");

        // Selective retry exclusive claim per LogicalOperationId
        var logical = new Domain.LogicalOperationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        db.RetryItemClaims.Add(new Domain.RetryItemClaim
        {
            LogicalOperationId = logical,
            ClaimedByRequestId = Guid.NewGuid()
        });
        bool secondClaimWouldFail = db.RetryItemClaims.Any(c => c.LogicalOperationId == logical);
        Console.WriteLine($"  RetryItemClaims unique LogicalOperationId; conflict detect={secondClaimWouldFail}");
        Console.WriteLine("  See EfRetryClaimStore + docs/idempotency.md");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 13 — Saga + approval mapping sketches
// =============================================================================
public static class Part13_SagaApprovalMapping
{
    public static void Run()
    {
        Console.WriteLine("-- PART 13: Saga + approval mapping sketches --");
        var request = new Domain.LicenseExtensionRequest();
        request.Items.Add(new Domain.LicenseExtensionRequestItem
        {
            EntitlementId = "CS-1",
            Provider = Domain.ProviderSystem.CloudShield,
            CurrentExpiryUtc = DateTimeOffset.UtcNow,
            RequestedExpiryUtc = DateTimeOffset.UtcNow.AddDays(120),
        });
        request.Items.Add(new Domain.LicenseExtensionRequestItem
        {
            EntitlementId = "NG-1",
            Provider = Domain.ProviderSystem.NetworkGuard,
            CurrentExpiryUtc = DateTimeOffset.UtcNow,
            RequestedExpiryUtc = DateTimeOffset.UtcNow.AddDays(120),
        });

        var band = Domain.ApprovalBand.RenewalManagerThenDealDesk;
        var approval = new Domain.ApprovalCase
        {
            RequestId = request.Id,
            Band = band,
            Stages =
            {
                new Domain.ApprovalStage { Role = "RenewalManager", Ordinal = 1 },
                new Domain.ApprovalStage { Role = "DealDeskManager", Ordinal = 2 },
            }
        };

        var saga = new Domain.LicenseExtensionSaga
        {
            RequestId = request.Id,
            State = Domain.SagaState.PendingRenewalManagerApproval,
            RequiresDealDesk = true,
            ItemOutcomes = request.Items.Select(i => new Domain.SagaItemOutcome
            {
                LogicalOperationId = i.LogicalOperationId,
                Status = "Pending"
            }).ToList(),
            Transitions =
            {
                new Domain.SagaTransition
                {
                    FromState = Domain.SagaState.Requested,
                    ToState = Domain.SagaState.PendingRenewalManagerApproval,
                    Reason = "Band RM→DealDesk"
                }
            }
        };

        Console.WriteLine($"  request items={request.Items.Count} approval stages={approval.Stages.Count}");
        Console.WriteLine($"  saga state={saga.State} outcomes={saga.ItemOutcomes.Count}");
        Console.WriteLine("  Persist request + approval + saga + outbox in ONE SaveChangesAsync unit-of-work.");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 14 — Simulator databases
// =============================================================================
public static class Part14_SimulatorDatabases
{
    public static void Run()
    {
        Console.WriteLine("-- PART 14: cloudshield / networkguard DBs --");
        Console.WriteLine("""
  CloudShield (cloudshield DB):
    - Entitlements, applied license operations
    - Idempotent apply keyed by LogicalOperationId/AttemptId from commands
    - Consumes Kafka topic cloudshield.license-extension.commands

  NetworkGuard (networkguard DB):
    - Licenses, applied operations
    - Same idempotent apply pattern
    - Consumes networkguard.license-extension.commands

  Platform NEVER writes simulator tables directly — only via provider HTTP/Kafka.
  Reconciliation compares platform saga outcomes vs provider applied ops.
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 15 — Interview notes
// =============================================================================
public static class Part15_Interview
{
    public static void Print()
    {
        Console.WriteLine("-- PART 15: Interview notes + anti-patterns --");
        Console.WriteLine("""
  DO
  □ Treat EntitleXDbContext as the unit-of-work boundary for a use case.
  □ Use AsNoTracking for read-only paths; track only when mutating.
  □ Prefer keyset pagination for pending approvals / audit scans.
  □ Disable AutoDetectChanges for bulk attach of large item graphs.
  □ Claim outbox with FOR UPDATE SKIP LOCKED + lease timeout.
  □ Map row_version as concurrency token; translate conflicts to HTTP 409.
  □ Keep three DBs separate — platform vs CloudShield vs NetworkGuard.

  DON'T
  □ Hold DbContext in a Singleton.
  □ Use OFFSET deep paging for hot approval queues.
  □ Call DetectChanges on every item add in a 1000-item request.
  □ Publish to Kafka inside the request transaction (use outbox).
  □ Re-fulfill when saga is ReconciliationRequired without an ops decision.
  □ Change LogicalOperationId on selective retry (keep stable; new AttemptId).

  Day bands reminder: 1–30 auto, 31–90 RM, 91–365 RM→DealDesk, >365 unsupported.
  Roles: RenewalRepresentative / RenewalManager / DealDeskManager / PlatformAdmin.
""");
    }
}
}
