// =============================================================================
 // EntitleX .NET PERFORMANCE & MEMORY GUIDE
 // Interview-style deep dive framed around OutboxPublisher, FulfillmentWorker,
 // EF bulk attach of ≤1000 LicenseExtensionRequest items, and provider fan-out.
 //
 // Target: .NET 8  |  No external NuGet packages required for demos.
 //
 // Real paths referenced:
 //   src/EntitleX.Infrastructure/Persistence/Repositories/LicenseExtensionRequestRepository.cs
 //   src/EntitleX.Infrastructure/DependencyInjection.cs          (Npgsql MaxBatchSize(100))
 //   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
 //   src/EntitleX.Infrastructure/Outbox/OutboxPublisherService.cs
 //   src/workers/EntitleX.OutboxPublisher/
 //   src/workers/EntitleX.FulfillmentWorker/
 //   src/EntitleX.Messaging/Sqs/SqsMessagePublisher.cs
 //   docs/performance.md  |  docs/observability.md
 //
 // TABLE OF CONTENTS
 //   PART 1  — GC generations & what EntitleX allocates
 //   PART 2  — Allocation hotspots (correlation strings, boxing IDs, LINQ)
 //   PART 3  — ArrayPool for bulk payloads
 //   PART 4  — class vs struct IDs (readonly record struct)
 //   PART 5  — Span parsing message keys / entitlement ids
 //   PART 6  — Memory leaks (event handlers on long-lived workers)
 //   PART 7  — IDisposable (HttpClient / DbContext scopes)
 //   PART 8  — async Task vs ValueTask on hot paths
 //   PART 9  — EF / Npgsql bulk patterns (AutoDetectChanges, MaxBatchSize)
 //   PART 10 — Object pools, StringBuilder, avoid chatty logs
 //   PART 11 — Tooling (dotnet-counters, OTel metrics :9180+)
 //   PART 12 — Benchmarks: LINQ vs loop over saga items
 //   PART 13 — Caching realities (no Redis)
 //   PART 14 — Interview Q&A
 //   PART 15 — Cheat sheet (OutboxPublisher / FulfillmentWorker)
 // =============================================================================

#nullable enable
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EntitleX.Guides.PerformanceAndMemory;

// -----------------------------------------------------------------------------
 // Simulated domain types (mirror BuildingBlocks / workers — self-contained).
 // -----------------------------------------------------------------------------

public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct LogicalOperationId(Guid Value)
{
    public static LogicalOperationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct AttemptId(Guid Value)
{
    public static AttemptId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct EntitlementId(string Value);

public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());
}

public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public enum ProviderSystem { CloudShield = 1, NetworkGuard = 2 }

public enum SagaState
{
    QueuedForFulfillment, Processing, PartiallyCompleted, Completed, Failed,
    ReconciliationRequired
}

public sealed class SagaItemDemo
{
    public LogicalOperationId LogicalOperationId { get; init; }
    public EntitlementId EntitlementId { get; init; }
    public ProviderSystem Provider { get; init; }
    public string Status { get; set; } = "Pending";
}

public sealed class OutboxMessageDemo
{
    public MessageId Id { get; init; }
    public string MessageType { get; init; } = "";
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public EntitlementId? PartitionKey { get; init; }
}

// =============================================================================
public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX Performance & Memory Guide (.NET 8) ===\n");

        Part1_GcGenerations();
        Part2_AllocationHotspots();
        Part3_ArrayPool();
        Part4_ClassVsStructIds();
        Part5_SpanParsing();
        Part6_LeakPatterns();
        Part7_IDisposable();
        await Part8_TaskVsValueTask();
        Part9_EfBulkPatterns();
        Part10_PoolsAndLogging();
        Part11_Tooling();
        Part12_LinqVsLoop();
        Part13_CachingNoRedis();
        Part14_InterviewQA();
        Part15_CheatSheet();

        Console.WriteLine("\n=== Done. Pair with docs/performance.md in interviews. ===");
    }

    // =========================================================================
    // PART 1 — GC generations
    // =========================================================================
    static void Part1_GcGenerations()
    {
        Section("PART 1 — GC generations & what EntitleX allocates");

        // Gen0: short-lived — per-request DTOs, MediatR handler locals, JSON buffers.
        // Gen1: survivors — rare; watch for accidental captures in long Polly pipelines.
        // Gen2 / LOH: large outbox payload byte[] (>85KB), big Include graphs, log scopes.
        //
        // Workstation GC (default for API) vs Server GC (workers often benefit).
        Console.WriteLine($"IsServerGC = {System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine($"LatencyMode = {System.Runtime.GCSettings.LatencyMode}");
        Console.WriteLine($"Total memory (approx) = {GC.GetTotalMemory(forceFullCollection: false):N0} bytes");

        // Force a gen0 churn demo similar to building correlation log strings.
        var before0 = GC.CollectionCount(0);
        for (int i = 0; i < 10_000; i++)
        {
            _ = $"corr={CorrelationId.New()} req={RequestId.New()}";
        }
        var after0 = GC.CollectionCount(0);
        Console.WriteLine($"Gen0 collections during string-concat churn: {after0 - before0}");
        Console.WriteLine("Prefer structured Serilog properties over interpolated high-volume strings.");
    }

    // =========================================================================
    // PART 2 — Allocation hotspots
    // =========================================================================
    static void Part2_AllocationHotspots()
    {
        Section("PART 2 — Allocation hotspots");

        // HOTSPOT A: string concat of correlation / request / logical ids in tight loops.
        var corr = CorrelationId.New();
        var req = RequestId.New();
        var logical = LogicalOperationId.New();

        // Bad (interview answer): builds multiple intermediates.
        string bad = "corr=" + corr.ToString() + " req=" + req.ToString() + " lop=" + logical.ToString();
        Console.WriteLine($"Bad concat length = {bad.Length}");

        // Better: StringBuilder when appending many segments repeatedly.
        var sb = new StringBuilder(96);
        sb.Append("corr=").Append(corr).Append(" req=").Append(req).Append(" lop=").Append(logical);
        Console.WriteLine($"StringBuilder length = {sb.Length}");

        // Best for logs: Serilog templates — no metric labels for these IDs
        // (docs/observability.md: never Prometheus-label requestId / messageId / logicalOperationId).
        Console.WriteLine("Log as properties: {CorrelationId} {RequestId} {LogicalOperationId}");

        // HOTSPOT B: boxing readonly record struct IDs when cast to object / non-generic collections.
        object boxed = RequestId.New(); // boxes the struct
        Console.WriteLine($"Boxed RequestId type = {boxed.GetType().Name} (avoid ArrayList / object params)");

        // HOTSPOT C: LINQ over Items when a simple loop suffices on hot fulfillment paths.
        var items = CreateItems(200);
        var linqFailed = items.Where(i => i.Status == "Failed").Select(i => i.LogicalOperationId).ToList();
        Console.WriteLine($"LINQ failed count = {linqFailed.Count} (fine at ≤1000; measure in workers)");

        // HOTSPOT D: closure captures allocating display-class objects in loops.
        Action? leaky = null;
        for (int i = 0; i < 3; i++)
        {
            var local = items[i];
            leaky += () => Console.WriteLine(local.EntitlementId.Value);
        }
        Console.WriteLine($"Multicast delegate target count rough size concern — prefer explicit loops in workers");
        _ = leaky;
    }

    // =========================================================================
    // PART 3 — ArrayPool for bulk payloads
    // =========================================================================
    static void Part3_ArrayPool()
    {
        Section("PART 3 — ArrayPool for bulk payloads");

        // OutboxPublisher / FulfillmentWorker often serialize fulfillment commands
        // covering many entitlements. Rent buffers instead of new byte[size] every time.
        ArrayPool<byte> pool = ArrayPool<byte>.Shared;
        int payloadSize = 8_192; // typical small command JSON
        byte[] buffer = pool.Rent(payloadSize);
        try
        {
            var msg = new
            {
                requestId = RequestId.New().ToString(),
                attemptId = AttemptId.New().ToString(),
                provider = nameof(ProviderSystem.CloudShield),
                partitionKey = "ent-cloudshield-001"
            };
            var written = JsonSerializer.SerializeToUtf8Bytes(msg);
            written.CopyTo(buffer.AsSpan());
            Console.WriteLine($"Rented {buffer.Length} bytes (may be > requested); wrote {written.Length}");
            // IMPORTANT: clear if buffer held secrets / tokens before Return.
        }
        finally
        {
            pool.Return(buffer, clearArray: true);
        }

        // LOH avoidance: arrays >= ~85_000 bytes land on Large Object Heap.
        // Prefer pooled buffers + streaming JSON for large 1000-item payloads.
        Console.WriteLine("For MaxItems=1000 graphs prefer streaming / chunked publish over giant LOH arrays.");
    }

    // =========================================================================
    // PART 4 — class vs struct IDs
    // =========================================================================
    static void Part4_ClassVsStructIds()
    {
        Section("PART 4 — class vs struct IDs");

        // EntitleX IDs are readonly record structs (RequestId, LogicalOperationId, AttemptId,
        // MessageId, CorrelationId). EntitlementId is readonly record struct wrapping string.
        // Why struct?
        //   • No heap allocation per id when kept on stack / in arrays of structs
        //   • Value equality for Dictionary/HashSet keys
        //   • Prevents null RequestId (use nullable RequestId? for ParentId lineage)
        //
        // Tradeoff: copying large structs is costly — keep ID structs tiny (Guid / string ref).

        var ids = new LogicalOperationId[1000];
        for (int i = 0; i < ids.Length; i++) ids[i] = LogicalOperationId.New();
        Console.WriteLine($"struct ID array length = {ids.Length} (contiguous value copies)");

        // ParentId lineage on selective retry: RequestId? parentId — nullable struct, no heap.
        RequestId? parentId = RequestId.New();
        RequestId child = RequestId.New();
        Console.WriteLine($"Selective retry child={child} parent={parentId} (LogicalOperationId preserved, AttemptId new)");

        // Anti-pattern: class-based Id with reference equality → HashSet bugs.
        Console.WriteLine("Never use reference equality for LogicalOperationId membership checks.");
    }

    // =========================================================================
    // PART 5 — Span parsing
    // =========================================================================
    static void Part5_SpanParsing()
    {
        Section("PART 5 — Span parsing message keys");

        // Message / Kafka keys often look like: "entitlement:{entitlementId}" or
        // "fulfillment:{requestId}:{messageId}". Span avoids Substring allocations.
        string key = $"fulfillment:{RequestId.New()}:{MessageId.New()}";
        ParseFulfillmentKey(key, out var kind, out var requestIdText, out var messageIdText);
        Console.WriteLine($"Parsed kind={kind} requestIdLen={requestIdText.Length} messageIdLen={messageIdText.Length}");

        // EntitlementId partition key — Kafka ordering per entitlement.
        ReadOnlySpan<char> partition = "ent-networkguard-014".AsSpan();
        Console.WriteLine($"Partition key starts with ent-? {partition.StartsWith("ent-")}");
    }

    static void ParseFulfillmentKey(
        string key,
        out string kind,
        out string requestId,
        out string messageId)
    {
        ReadOnlySpan<char> span = key.AsSpan();
        int c1 = span.IndexOf(':');
        int c2 = span[(c1 + 1)..].IndexOf(':') + c1 + 1;
        kind = span[..c1].ToString(); // unavoidable if API needs string; keep spans longer when possible
        requestId = span[(c1 + 1)..c2].ToString();
        messageId = span[(c2 + 1)..].ToString();
    }

    // =========================================================================
    // PART 6 — Leaks
    // =========================================================================
    static void Part6_LeakPatterns()
    {
        Section("PART 6 — Memory leaks on long-lived workers");

        // OutboxPublisher and FulfillmentWorker are HostedService loops — they live for days.
        // Classic leak: static/event subscription that captures DbContext / HttpResponse.
        var bus = new FakeDomainEventBus();
        var handler = new FakeTransientHandler();
        bus.OutboxPublished += handler.OnPublished; // DANGER if handler is per-iteration scoped
        bus.Publish(MessageId.New());
        bus.OutboxPublished -= handler.OnPublished; // always unsubscribe or use weak patterns
        Console.WriteLine("Unsubscribed transient handler from long-lived worker event.");

        // Another leak: IAsyncEnumerable / ChannelReader not completing → buffers grow.
        // Another: unbounded ConcurrentDictionary as "cache" of CorrelationId → memory death.
        // EntitleX: durable processed_messages in Postgres; no Redis; don't invent immortal caches.
        Console.WriteLine("Prefer scoped DI per poll iteration (see OutboxPublisher Worker creating scopes).");
    }

    // =========================================================================
    // PART 7 — IDisposable
    // =========================================================================
    static void Part7_IDisposable()
    {
        Section("PART 7 — IDisposable (HttpClient / DbContext)");

        // HttpClient: do NOT dispose a typed client per call — use IHttpClientFactory.
        // EntitleX registers named provider clients (CloudShield / NetworkGuard) with
        // Polly bulkhead; command clients omit retry on POST (recon instead).
        Console.WriteLine("IHttpClientFactory → named clients CloudShield/NetworkGuard (bulkhead, no POST retry).");

        // DbContext: scoped lifetime. Workers create IServiceScope per loop iteration.
        using (var scope = new FakeDbContextScope())
        {
            scope.Context.Touch();
            Console.WriteLine("Fake DbContext used inside using/scope — disposed at end of poll.");
        }

        // SemaphoreSlim / CancellationTokenSource: dispose when you own them.
        // SqsMessagePublisher holds a SemaphoreSlim for resolve — disposed with publisher.
        var gate = new SemaphoreSlim(1, 1);
        gate.Dispose();
        Console.WriteLine("SemaphoreSlim disposed (owner = SqsMessagePublisher analogy).");
    }

    // =========================================================================
    // PART 8 — Task vs ValueTask
    // =========================================================================
    static async Task Part8_TaskVsValueTask()
    {
        Section("PART 8 — async Task vs ValueTask");

        // Hot cache hit path for processed MessageId checks can return ValueTask
        // to avoid allocating a Task when the result is already synchronous.
        var cache = new ProcessedMessageHotCache();
        var id = MessageId.New();
        cache.Remember(id);

        ValueTask<bool> hit = cache.HasProcessedAsync(id);
        Console.WriteLine($"ValueTask cache hit (sync completion) → {await hit}");

        // Rules of thumb for EntitleX handlers:
        //   • Public MediatR handlers: Task (simpler, composing-friendly)
        //   • Extremely hot internal helpers called millions of times: consider ValueTask
        //   • Never await a ValueTask twice; never use it after GetAwaiter().GetResult() messily
        //   • Channels / EF usually return Task — don't wrap just for fashion

        Task whenAll = Task.WhenAll(
            SimulateProviderCall(ProviderSystem.CloudShield),
            SimulateProviderCall(ProviderSystem.NetworkGuard));
        await whenAll;
        Console.WriteLine("Task.WhenAll provider fan-out completed (aggregation path).");
    }

    static async Task SimulateProviderCall(ProviderSystem provider)
    {
        await Task.Delay(10);
        Console.WriteLine($"  provider {provider} latency simulated");
    }

    // =========================================================================
    // PART 9 — EF / Npgsql bulk patterns
    // =========================================================================
    static void Part9_EfBulkPatterns()
    {
        Section("PART 9 — EF / Npgsql bulk patterns");

        // From LicenseExtensionRequestRepository + DependencyInjection:
        //   • MaxItems = 1000 on LicenseExtensionRequest
        //   • AutoDetectChangesEnabled = false while attaching the graph
        //   • Npgsql MaxBatchSize(100) → ~10 round-trips for 1000 inserts
        //   • Cascade ownership — no separate COPY path in current phase

        var attached = 0;
        bool autoDetect = true;
        try
        {
            autoDetect = false; // simulate ChangeTracker.AutoDetectChangesEnabled = false
            for (int i = 0; i < 1000; i++)
            {
                attached++; // Attach item entity
            }
            // SaveChanges → batched inserts of 100
            int batches = (int)Math.Ceiling(attached / 100.0);
            Console.WriteLine($"Attached {attached} items; MaxBatchSize(100) ⇒ ~{batches} batches");
        }
        finally
        {
            autoDetect = true;
            Console.WriteLine($"Restored AutoDetectChangesEnabled={autoDetect}");
        }

        // AsNoTracking for saga GET / audit / retry claims existence checks.
        Console.WriteLine("AsNoTracking for read-only paths; keep tracking for orchestrator mutations.");
        // Keyset pagination beats OFFSET on audit histories.
        Console.WriteLine("Audit keyset: (occurred_at, id) — see docs/performance.md");
    }

    // =========================================================================
    // PART 10 — Pools & logging
    // =========================================================================
    static void Part10_PoolsAndLogging()
    {
        Section("PART 10 — Object pools, StringBuilder, chatty logs");

        // DefaultInterpolatedStringHandler (.NET 6+) reduces allocs for many interpolations,
        // but high-cardinality logs per item in a 1000-item request still hurt I/O.
        var requestId = RequestId.New();
        Console.WriteLine($"Request-level log once: Processing {requestId} with 1000 items");
        Console.WriteLine("Do NOT log per-item at Information in FulfillmentWorker hot path.");

        // Metric cardinality: entitlex_fulfillment_outcomes_total{outcome=...}
        // NEVER put LogicalOperationId on Prometheus labels (observability.md).
        Console.WriteLine("Metrics ports: OutboxPublisher :9180, FulfillmentWorker :9181, Recon :9182, Notify :9183");
    }

    // =========================================================================
    // PART 11 — Tooling
    // =========================================================================
    static void Part11_Tooling()
    {
        Section("PART 11 — Tooling");

        Console.WriteLine("""
          Practical tooling for EntitleX workers:
          • dotnet-counters monitor --process-id <pid> System.Runtime Microsoft.AspNetCore.Hosting
          • dotnet-trace / dotnet-gcdump when Gen2 climbs on OutboxPublisher
          • OTel → Jaeger (OTLP :4317); Prometheus scrapes :9180+ HttpListener metrics
          • Serilog structured logs with CorrelationId enricher — join to W3C trace
          • Npgsql / EF logging: brief in prod; verbose only in local Compose
          • Grafana dashboard "EntitleX Overview" — backlog gauges, not per-id series
          """);

        // Cheap in-process counters for demos:
        var sw = Stopwatch.StartNew();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        _ = Enumerable.Range(0, 1000).Select(_ => LogicalOperationId.New()).ToArray();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();
        Console.WriteLine($"Allocated ~{allocAfter - allocBefore:N0} bytes creating 1000 LogicalOperationId values in {sw.ElapsedMilliseconds} ms");
    }

    // =========================================================================
    // PART 12 — LINQ vs loop
    // =========================================================================
    static void Part12_LinqVsLoop()
    {
        Section("PART 12 — Benchmarks: LINQ vs loop");

        var items = CreateItems(1000);
        // Mark ~10% failed for selective-retry selection analogy.
        for (int i = 0; i < items.Count; i += 10)
            items[i].Status = "Failed";

        var sw = Stopwatch.StartNew();
        HashSet<LogicalOperationId> viaLinq = items
            .Where(i => i.Status == "Failed")
            .Select(i => i.LogicalOperationId)
            .ToHashSet();
        sw.Stop();
        var linqMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var viaLoop = new HashSet<LogicalOperationId>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Status == "Failed")
                viaLoop.Add(items[i].LogicalOperationId);
        }
        sw.Stop();
        var loopMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"LINQ failed set: {viaLinq.Count} in {linqMs:F3} ms");
        Console.WriteLine($"Loop failed set: {viaLoop.Count} in {loopMs:F3} ms");
        Console.WriteLine("At MaxItems=1000 both are fine in domain selectors; optimize workers if profiles demand.");
    }

    // =========================================================================
    // PART 13 — Caching (no Redis)
    // =========================================================================
    static void Part13_CachingNoRedis()
    {
        Section("PART 13 — Caching realities (no Redis)");

        // EntitleX portfolio: no Redis. Idempotency and claims live in Postgres:
        //   processed_messages, api_idempotency_records, retry_item_claims,
        //   unique logical_operation_id indexes on outcomes / applied ops.
        Console.WriteLine("Shared cache ≠ ConcurrentDictionary in one worker process.");
        Console.WriteLine("Use DB uniqueness + SKIP LOCKED; don't introduce Redis for interview vibes.");

        // MemoryCache is OK for *process-local* ApprovalRules snapshots if ever needed —
        // but approval bands today are pure functions of extension days.
        Console.WriteLine("ApprovalBand is computed from max extension days — no cache required.");
    }

    // =========================================================================
    // PART 14 — Interview Q&A
    // =========================================================================
    static void Part14_InterviewQA()
    {
        Section("PART 14 — Interview Q&A");

        Console.WriteLine("""
          Q: How do you insert 1000 items without melting EF?
          A: Disable AutoDetectChanges while attaching; Npgsql MaxBatchSize(100); cascade graph.

          Q: Why readonly record struct for LogicalOperationId?
          A: Value equality, no null, cheap keys; preserved across selective retries.

          Q: Where do allocations hurt OutboxPublisher?
          A: Per-message JSON, stringbing correlation in logs, unbounded in-memory buffers.

          Q: Task vs ValueTask?
          A: Default Task; ValueTask only for hot sync-complete internals; don't expose casually.

          Q: How do you observe memory in Compose?
          A: dotnet-counters + Prometheus :9180+; watch Gen2 / LOH / outbox backlog gauge.

          Q: Why no Redis?
          A: Postgres already provides idempotency tables & SKIP LOCKED claims; fewer moving parts.
          """);
    }

    // =========================================================================
    // PART 15 — Cheat sheet
    // =========================================================================
    static void Part15_CheatSheet()
    {
        Section("PART 15 — Cheat sheet (OutboxPublisher / FulfillmentWorker)");

        Console.WriteLine("""
          OutboxPublisher
            • Claim batch: FOR UPDATE SKIP LOCKED (OutboxClaimer)
            • Scope per poll iteration; dispose DbContext promptly
            • Publish SQS then mark Published; backoff → Poison
            • Metrics: http://localhost:9180/metrics  (entitlex_outbox_backlog)

          FulfillmentWorker
            • SQS commands → provider Kafka fan-out (key=entitlement id)
            • Task.WhenAll bounded to CloudShield + NetworkGuard
            • SemaphoreSlim around SQS resolve (SqsMessagePublisher)
            • Polly bulkhead on GET; no retry on extension POST
            • Metrics: http://localhost:9181/metrics

          Shared
            • MaxItems 1000 • MaxBatchSize 100 • AsNoTracking reads
            • Forward recovery: never demote successful LogicalOperationId
            • Correlation in logs/traces — never as Prometheus labels
          """);
    }

    // --- helpers ---

    static List<SagaItemDemo> CreateItems(int n)
    {
        var list = new List<SagaItemDemo>(n);
        for (int i = 0; i < n; i++)
        {
            list.Add(new SagaItemDemo
            {
                LogicalOperationId = LogicalOperationId.New(),
                EntitlementId = new EntitlementId($"ent-{i:D4}"),
                Provider = i % 2 == 0 ? ProviderSystem.CloudShield : ProviderSystem.NetworkGuard,
                Status = "Pending"
            });
        }
        return list;
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 72));
    }
}

// -----------------------------------------------------------------------------
 // Support fakes
 // -----------------------------------------------------------------------------

sealed class FakeDomainEventBus
{
    public event Action<MessageId>? OutboxPublished;
    public void Publish(MessageId id) => OutboxPublished?.Invoke(id);
}

sealed class FakeTransientHandler
{
    public void OnPublished(MessageId id) =>
        Console.WriteLine($"  handler saw MessageId={id}");
}

sealed class FakeDbContextScope : IDisposable
{
    public FakeDbContext Context { get; } = new();
    public void Dispose() => Context.Dispose();
}

sealed class FakeDbContext : IDisposable
{
    public void Touch() { }
    public void Dispose() => Console.WriteLine("  FakeDbContext.Dispose()");
}

/// <summary>Process-local hot cache — NOT a substitute for processed_messages.</summary>
sealed class ProcessedMessageHotCache
{
    private readonly HashSet<MessageId> _set = new();

    public void Remember(MessageId id) => _set.Add(id);

    public ValueTask<bool> HasProcessedAsync(MessageId id)
    {
        // Synchronous completion → no Task allocation.
        return new ValueTask<bool>(_set.Contains(id));
    }
}
