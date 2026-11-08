// =============================================================================
 // EntitleX .NET CONCURRENCY & MULTITHREADING GUIDE
 // Interview-style tour of async handlers, outbox claiming, SQS publish gates,
 // selective-retry races, and optimistic concurrency — all through EntitleX.
 //
 // Target: .NET 8  |  No external NuGet packages required.
 //
 // Real paths referenced:
 //   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs          (SKIP LOCKED)
 //   src/EntitleX.Infrastructure/Outbox/OutboxPublisherService.cs
 //   src/EntitleX.Messaging/Sqs/SqsMessagePublisher.cs             (SemaphoreSlim)
 //   src/workers/EntitleX.OutboxPublisher/Worker.cs
 //   src/workers/EntitleX.FulfillmentWorker/
 //   src/EntitleX.Domain/LicenseExtension/LicenseExtensionRequest.cs (RowVersion)
 //   docs/idempotency.md  |  docs/saga-workflow.md
 //
 // TABLE OF CONTENTS
 //   PART 1  — Threads vs Tasks vs async I/O
 //   PART 2  — async/await pitfalls in MediatR handlers
 //   PART 3  — CancellationToken through MediatR / workers
 //   PART 4  — lock vs SemaphoreSlim (SQS publisher resolve)
 //   PART 5  — ConcurrentDictionary patterns
 //   PART 6  — Channels as outbox drain analogy
 //   PART 7  — Parallel / Task.WhenAll entitlement aggregation
 //   PART 8  — Race on selective retry claims
 //   PART 9  — Optimistic concurrency (RowVersion)
 //   PART 10 — Barrier / CountdownEvent (light touch)
 //   PART 11 — ConcurrentQueue / Bag / Stack
 //   PART 12 — Deadlocks, sync-over-async, ConfigureAwait
 //   PART 13 — Polly bulkhead & circuit breaker (conceptual)
 //   PART 14 — Interview Q&A (EntitleX scenarios)
 //   PART 15 — Cheat sheet
 // =============================================================================

#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace EntitleX.Guides.Concurrency;

// -----------------------------------------------------------------------------
 // Simulated domain vocabulary
 // -----------------------------------------------------------------------------

public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct LogicalOperationId(Guid Value)
{
    public static LogicalOperationId New() => new(Guid.NewGuid());
}

public readonly record struct AttemptId(Guid Value)
{
    public static AttemptId New() => new(Guid.NewGuid());
}

public readonly record struct EntitlementId(string Value);

public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());
}

public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());
}

public enum ProviderSystem { CloudShield = 1, NetworkGuard = 2 }

public enum SagaState
{
    Requested, Approved, QueuedForFulfillment, Processing,
    PartiallyCompleted, Completed, Failed, ReconciliationRequired, Escalated
}

public enum OutboxStatus { Pending, Locked, Published, Poison }

public sealed class OutboxMessage
{
    public MessageId Id { get; init; } = MessageId.New();
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public string MessageType { get; init; } = "FulfillmentCommand";
    public EntitlementId? PartitionKey { get; init; }
    public DateTimeOffset AvailableAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class LicenseExtensionRequestConcurrencyDemo
{
    public RequestId Id { get; init; } = RequestId.New();
    public uint RowVersion { get; set; }
    public string Status { get; set; } = "InFulfillment";
}

// =============================================================================
public static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX Concurrency & Multithreading Guide (.NET 8) ===\n");

        Part1_ThreadsVsTasks();
        await Part2_AsyncAwaitPitfalls();
        await Part3_CancellationTokens();
        await Part4_LockVsSemaphoreSlim();
        Part5_ConcurrentDictionary();
        await Part6_ChannelsOutboxAnalogy();
        await Part7_WhenAllFanOut();
        await Part8_SelectiveRetryRace();
        Part9_OptimisticConcurrency();
        await Part10_BarrierCountdown();
        Part11_ConcurrentCollections();
        await Part12_DeadlocksAndSyncOverAsync();
        Part13_PollyBulkheadConceptual();
        Part14_InterviewQA();
        Part15_CheatSheet();

        Console.WriteLine("\n=== Done. Rehearse PART 14 with docs/idempotency.md open. ===");
    }

    // =========================================================================
    // PART 1 — Threads vs Tasks
    // =========================================================================
    static void Part1_ThreadsVsTasks()
    {
        Section("PART 1 — Threads vs Tasks vs async I/O");

        // Thread: OS scheduling unit. Creating many threads for SQS/Kafka I/O is wasteful.
        // Task: promise of work; async I/O Tasks do not occupy a thread while awaiting.
        // EntitleX workers: HostedService loops awaiting EF/SQS/Kafka — async all the way.

        Console.WriteLine($"Environment.ProcessorCount = {Environment.ProcessorCount}");
        Console.WriteLine($"ThreadPool workers available (heuristic) — prefer async I/O over Thread.Start");

        // Thread.Start demo (what NOT to do for outbox publish):
        var t = new Thread(() =>
        {
            Console.WriteLine($"  Dedicated thread {Environment.CurrentManagedThreadId} — avoid for I/O waits");
        });
        t.IsBackground = true;
        t.Start();
        t.Join();

        // Task.Run: offload CPU work (hashing ApprovedScopeHash) — not for wrapping already-async EF.
        Console.WriteLine("Use Task.Run for CPU-bound; never 'Task.Run(() => db.SaveChangesAsync())' casually.");
    }

    // =========================================================================
    // PART 2 — async/await pitfalls in handlers
    // =========================================================================
    static async Task Part2_AsyncAwaitPitfalls()
    {
        Section("PART 2 — async/await pitfalls in MediatR handlers");

        // Pitfall 1: async void — never in handlers (exceptions disappear).
        // Pitfall 2: holding a DB transaction across await SQS/Kafka/HTTP.
        //            EntitleX rule: Postgres transaction ends before outbox publish I/O.
        //            OutboxPublisher reads committed rows and publishes afterward.
        Console.WriteLine("API writes outbox_messages in same TX as saga/request; publish is async after commit.");

        // Pitfall 3: forgetting await — fire-and-forget publish loses failures.
        await SimulateHandlerAsync(CancellationToken.None);

        // Pitfall 4: capturing DbContext across parallel awaits (not thread-safe).
        //            Fan-out HTTP with WhenAll is OK; don't share one context across tasks.
        Console.WriteLine("EF DbContext is not thread-safe — one context per concurrent unit of work.");
    }

    static async Task SimulateHandlerAsync(CancellationToken ct)
    {
        // MediatR-style handler body:
        // 1) load aggregate  2) mutate  3) save + outbox  4) return DTO
        await Task.Delay(5, ct);
        Console.WriteLine("  RetryFailedLicenseExtensionsCommandHandler analogy awaited cleanly.");
    }

    // =========================================================================
    // PART 3 — CancellationToken
    // =========================================================================
    static async Task Part3_CancellationTokens()
    {
        Section("PART 3 — CancellationToken through MediatR / workers");

        using var cts = new CancellationTokenSource();
        // HostedService receives stoppingToken — OutboxPublisher loop honors it.
        var workerLoop = RunOutboxPollOnceAsync(cts.Token);

        // Request abort (user cancel / Kestrel abort) should flow into handlers.
        cts.CancelAfter(50);
        try
        {
            await RunLongReconciliationAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("OperationCanceledException — cooperative cancel honored.");
        }

        try { await workerLoop; }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Outbox poll canceled via stopping token.");
        }

        // Linked tokens: combine request CT with a per-call timeout for provider GET.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Console.WriteLine($"Linked token cancelable? {linked.Token.CanBeCanceled}");
    }

    static async Task RunOutboxPollOnceAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(20, stoppingToken);
        }
    }

    static async Task RunLongReconciliationAsync(CancellationToken ct)
    {
        for (int i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct);
        }
    }

    // =========================================================================
    // PART 4 — lock vs SemaphoreSlim
    // =========================================================================
    static async Task Part4_LockVsSemaphoreSlim()
    {
        Section("PART 4 — lock vs SemaphoreSlim (SQS publisher)");

        // lock (Monitor): thin mutual exclusion for short CPU-bound critical sections.
        // Cannot await inside lock.
        var gate = new object();
        int published = 0;
        lock (gate)
        {
            published++;
        }
        Console.WriteLine($"lock protected counter = {published}");

        // SemaphoreSlim: async-friendly. SqsMessagePublisher uses SemaphoreSlim(1,1)
        // around URL resolve so concurrent publishes don't stampede GetQueueUrl.
        // Real: src/EntitleX.Messaging/Sqs/SqsMessagePublisher.cs
        var resolveLock = new SemaphoreSlim(1, 1);
        async Task PublishAsync(string queue, MessageId id)
        {
            await resolveLock.WaitAsync();
            try
            {
                await Task.Delay(5); // pretend resolve + send
                Console.WriteLine($"  SQS publish {id} → {queue}");
            }
            finally
            {
                resolveLock.Release();
            }
        }

        await Task.WhenAll(
            PublishAsync("entitlex-fulfillment", MessageId.New()),
            PublishAsync("entitlex-fulfillment", MessageId.New()));

        resolveLock.Dispose();
        Console.WriteLine("SemaphoreSlim also models Polly bulkhead (limited concurrent provider GETs).");
    }

    // =========================================================================
    // PART 5 — ConcurrentDictionary
    // =========================================================================
    static void Part5_ConcurrentDictionary()
    {
        Section("PART 5 — ConcurrentDictionary");

        // Process-local idempotency accelerator keyed by MessageId.
        // Durable source of truth remains processed_messages (Postgres) — no Redis.
        var processed = new ConcurrentDictionary<MessageId, byte>();

        Parallel.For(0, 100, _ =>
        {
            var id = MessageId.New();
            processed.TryAdd(id, 0);
        });
        Console.WriteLine($"ConcurrentDictionary entries ≈ {processed.Count}");

        // GetOrAdd for CorrelationId → Activity baggage in a single process (demo).
        var corrMap = new ConcurrentDictionary<CorrelationId, string>();
        var corr = CorrelationId.New();
        var name = corrMap.GetOrAdd(corr, static c => $"worker-{c}");
        Console.WriteLine($"GetOrAdd correlation tag = {name[..Math.Min(20, name.Length)]}...");

        // Caution: values that are mutable lists still need their own synchronization.
        Console.WriteLine("Don't store List<T> in ConcurrentDictionary without per-key locks.");
    }

    // =========================================================================
    // PART 6 — Channels as outbox drain
    // =========================================================================
    static async Task Part6_ChannelsOutboxAnalogy()
    {
        Section("PART 6 — Channels as outbox drain analogy");

        // Production: multiple OutboxPublisher instances claim with
        // FOR UPDATE SKIP LOCKED — no in-memory bus required.
        // Channel demos the single-process producer/consumer shape.

        var channel = Channel.CreateBounded<OutboxMessage>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producers = Enumerable.Range(0, 3).Select(async i =>
        {
            var msg = new OutboxMessage
            {
                PartitionKey = new EntitlementId($"ent-{i}"),
                Status = OutboxStatus.Pending
            };
            await channel.Writer.WriteAsync(msg);
        });
        await Task.WhenAll(producers);
        channel.Writer.Complete();

        int claimed = 0;
        await foreach (var msg in channel.Reader.ReadAllAsync())
        {
            msg.Status = OutboxStatus.Locked; // analogy — real SQL sets Locked
            claimed++;
            Console.WriteLine($"  Claimed {msg.Id} partition={msg.PartitionKey} (Kafka key = entitlement id)");
        }
        Console.WriteLine($"Channel drained count = {claimed}");
        Console.WriteLine("Real claim SQL lives in OutboxClaimer — SKIP LOCKED avoids publisher blocking.");
    }

    // =========================================================================
    // PART 7 — WhenAll fan-out
    // =========================================================================
    static async Task Part7_WhenAllFanOut()
    {
        Section("PART 7 — Parallel / Task.WhenAll entitlement aggregation");

        // Aggregation HttpClients fan out per provider (bounded to two), not per entitlement.
        // docs/performance.md — avoids N+1 HTTP.
        var sw = Stopwatch.StartNew();
        await Task.WhenAll(
            AggregateProviderAsync(ProviderSystem.CloudShield),
            AggregateProviderAsync(ProviderSystem.NetworkGuard));
        sw.Stop();
        Console.WriteLine($"Provider fan-out wall time ≈ {sw.ElapsedMilliseconds} ms (parallel)");

        // Parallel.ForEach: CPU-bound hashing of approved scopes — OK.
        // Do not Parallel.ForEach async lambdas that hit EF with one context.
        var entitlements = Enumerable.Range(0, 8)
            .Select(i => new EntitlementId($"ent-{i:D3}"))
            .ToArray();
        Parallel.ForEach(entitlements, e =>
        {
            _ = e.Value.GetHashCode(); // stand-in for ApprovedScopeHasher work
        });
        Console.WriteLine($"Parallel.ForEach hashed {entitlements.Length} entitlement ids");
    }

    static async Task AggregateProviderAsync(ProviderSystem provider)
    {
        await Task.Delay(30);
        Console.WriteLine($"  Aggregated snapshot from {provider}");
    }

    // =========================================================================
    // PART 8 — Selective retry race
    // =========================================================================
    static async Task Part8_SelectiveRetryRace()
    {
        Section("PART 8 — Race on selective retry claims");

        // Two concurrent POST /retry-failed for the same source LogicalOperationIds:
        //   retry_item_claims unique (source_request_id, logical_operation_id) → one wins, other 409
        //   api_idempotency_records unique key → lost-response replay returns prior retry
        // Forward recovery: successes never copied; LogicalOperationId preserved; AttemptId new.

        var source = RequestId.New();
        var failedLogical = new[] { LogicalOperationId.New(), LogicalOperationId.New() };
        var claimStore = new InMemoryRetryClaimStore();

        async Task<(bool ok, string detail)> AttemptRetry(string idempotencyKey)
        {
            await Task.Yield();
            if (!claimStore.TryClaim(source, failedLogical, idempotencyKey, out var retryId, out var reason))
                return (false, reason);
            return (true, $"created retry {retryId} AttemptId={AttemptId.New()}");
        }

        var results = await Task.WhenAll(
            AttemptRetry("key-A"),
            AttemptRetry("key-B")); // overlapping items, different idempotency keys

        foreach (var (ok, detail) in results)
            Console.WriteLine($"  {(ok ? "WIN" : "LOSE")}: {detail}");

        // Same idempotency key replay:
        var again = await AttemptRetry("key-A");
        Console.WriteLine($"  Replay key-A: {again.detail}");
    }

    // =========================================================================
    // PART 9 — Optimistic concurrency RowVersion
    // =========================================================================
    static void Part9_OptimisticConcurrency()
    {
        Section("PART 9 — Optimistic concurrency (RowVersion)");

        // LicenseExtensionRequest.RowVersion maps to Postgres row_version / xmin-style token.
        // Two orchestrators updating the same aggregate: one SaveChanges wins; other concurrency exception.
        var req = new LicenseExtensionRequestConcurrencyDemo { RowVersion = 1, Status = "InFulfillment" };

        bool TryUpdate(LicenseExtensionRequestConcurrencyDemo local, uint expected, string newStatus)
        {
            if (local.RowVersion != expected)
            {
                Console.WriteLine($"  Concurrency conflict: expected {expected}, actual {local.RowVersion}");
                return false;
            }
            local.Status = newStatus;
            local.RowVersion++; // DB would bump token
            return true;
        }

        var snapshotA = req.RowVersion;
        var snapshotB = req.RowVersion;
        Console.WriteLine($"TryUpdate A → {TryUpdate(req, snapshotA, "PartiallyFulfilled")}");
        Console.WriteLine($"TryUpdate B → {TryUpdate(req, snapshotB, "Failed")} (expect conflict)");
        Console.WriteLine($"Final status={req.Status} rowVersion={req.RowVersion}");
    }

    // =========================================================================
    // PART 10 — Barrier / CountdownEvent
    // =========================================================================
    static async Task Part10_BarrierCountdown()
    {
        Section("PART 10 — Barrier / CountdownEvent (light touch)");

        // Rarely needed in ASP.NET — shown for interview literacy.
        // Analogy: wait until both CloudShield and NetworkGuard simulators are "ready"
        // before starting an e2e fulfillment drill (test harness only).
        using var countdown = new CountdownEvent(2);
        void SignalReady(ProviderSystem p)
        {
            Console.WriteLine($"  {p} ready");
            countdown.Signal();
        }

        await Task.WhenAll(
            Task.Run(() => { Thread.Sleep(15); SignalReady(ProviderSystem.CloudShield); }),
            Task.Run(() => { Thread.Sleep(10); SignalReady(ProviderSystem.NetworkGuard); }));

        countdown.Wait(TimeSpan.FromSeconds(1));
        Console.WriteLine($"Countdown hit zero? {countdown.IsSet}");

        // Barrier: N participants cyclic sync — almost never in control-plane handlers.
        Console.WriteLine("Prefer Task.WhenAll over Barrier for provider fan-out.");
    }

    // =========================================================================
    // PART 11 — ConcurrentQueue / Bag / Stack
    // =========================================================================
    static void Part11_ConcurrentCollections()
    {
        Section("PART 11 — ConcurrentQueue / Bag / Stack");

        var poison = new ConcurrentQueue<MessageId>();
        poison.Enqueue(MessageId.New());
        poison.Enqueue(MessageId.New());
        Console.WriteLine($"Poison ConcurrentQueue TryDequeue → {poison.TryDequeue(out var mid)} ({mid})");

        var bag = new ConcurrentBag<SagaState>();
        bag.Add(SagaState.Processing);
        bag.Add(SagaState.ReconciliationRequired);
        Console.WriteLine($"ConcurrentBag count = {bag.Count} (unordered)");

        var stack = new ConcurrentStack<string>();
        stack.Push("Escalated");
        stack.Push("PendingRenewalManagerApproval");
        stack.TryPop(out var top);
        Console.WriteLine($"ConcurrentStack pop = {top}");
    }

    // =========================================================================
    // PART 12 — Deadlocks & sync-over-async
    // =========================================================================
    static async Task Part12_DeadlocksAndSyncOverAsync()
    {
        Section("PART 12 — Deadlocks, sync-over-async, ConfigureAwait");

        // Sync-over-async: .Result / .Wait() on UI/ASP.NET classic sync context → deadlock risk.
        // In modern ASP.NET Core there is no SynchronizationContext by default, but
        // .Result still wastes threads and surfaces AggregateException poorly.
        var task = Task.Delay(10);
        await task; // good
        Console.WriteLine("Prefer await over .Result in MediatR handlers and workers.");

        // ConfigureAwait(false): library code historically; app handlers usually plain await.
        await Task.Delay(1).ConfigureAwait(false);
        Console.WriteLine("ConfigureAwait(false) in BuildingBlocks libraries if ever needed.");

        // Deadlock classic: lock taken, then await work that needs same lock — use SemaphoreSlim.
        Console.WriteLine("Never await inside lock{}; use SemaphoreSlim for async mutual exclusion.");
    }

    // =========================================================================
    // PART 13 — Polly bulkhead (conceptual)
    // =========================================================================
    static void Part13_PollyBulkheadConceptual()
    {
        Section("PART 13 — Polly bulkhead & circuit breaker (conceptual)");

        // Named HttpClients: GET pipeline = timeout + retry + CB + bulkhead.
        // Command (POST extension) clients omit retry — uncertain POSTs go to recon.
        // File: ProviderHttpClientRegistration.cs
        Console.WriteLine("Bulkhead limits concurrent CloudShield/NetworkGuard GETs.");
        Console.WriteLine("Circuit breaker opens on provider failure storms — saga may go ReconciliationRequired.");
        Console.WriteLine("No client-side re-POST of extensions; selective retry is explicit + claimed.");
    }

    // =========================================================================
    // PART 14 — Interview Q&A
    // =========================================================================
    static void Part14_InterviewQA()
    {
        Section("PART 14 — Interview Q&A (EntitleX scenarios)");

        Console.WriteLine("""
          Q: Two OutboxPublisher replicas — who wins a row?
          A: FOR UPDATE SKIP LOCKED; each claims different Pending rows; no blocking wait.

          Q: Why not publish SQS inside the API request transaction?
          A: Avoid dual-write / long TX across network I/O. Outbox row + commit; worker publishes.

          Q: Concurrent selective retries of the same failed items?
          A: retry_item_claims unique → 409; idempotency key replay returns prior retry request.

          Q: How do you fan out to CloudShield and NetworkGuard safely?
          A: Task.WhenAll on two HttpClients; separate DbContexts; Kafka key=entitlement id.

          Q: What does RowVersion protect?
          A: Lost updates on LicenseExtensionRequest / saga aggregates under concurrent orchestrators.

          Q: lock or SemaphoreSlim for SQS URL resolve?
          A: SemaphoreSlim — need await; see SqsMessagePublisher._resolveLock.

          Q: Threads for Kafka consumers?
          A: Prefer async consumer loops / hosted services; don't spawn a Thread per partition manually.
          """);
    }

    // =========================================================================
    // PART 15 — Cheat sheet
    // =========================================================================
    static void Part15_CheatSheet()
    {
        Section("PART 15 — Cheat sheet");

        Console.WriteLine("""
          • async all the way in workers; no async void; no .Result in handlers
          • Pass CancellationToken from MediatR + HostedService stoppingToken
          • SemaphoreSlim for async mutual exclusion (SQS resolve, bulkheads)
          • ConcurrentDictionary = process-local only (no Redis in EntitleX)
          • Channel ≈ teaching model; production outbox = SKIP LOCKED
          • WhenAll for 2-provider fan-out; Parallel for CPU hashing
          • Selective retry: preserve LogicalOperationId, mint AttemptId, ParentId lineage
          • RowVersion optimistic concurrency on aggregates
          • Never hold DB TX across SQS/Kafka/HTTP
          • Forward recovery — never compensate successful extensions
          """);
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
 // In-memory stand-in for api_idempotency_records + retry_item_claims
 // -----------------------------------------------------------------------------

sealed class InMemoryRetryClaimStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RequestId> _idempotency = new(StringComparer.Ordinal);
    private readonly HashSet<(Guid Source, Guid Logical)> _claims = new();

    public bool TryClaim(
        RequestId source,
        IReadOnlyList<LogicalOperationId> logicalIds,
        string idempotencyKey,
        out RequestId retryId,
        out string reason)
    {
        lock (_gate)
        {
            if (_idempotency.TryGetValue(idempotencyKey, out var existing))
            {
                retryId = existing;
                reason = $"idempotent replay → {existing}";
                return true; // replay success path
            }

            foreach (var lop in logicalIds)
            {
                if (_claims.Contains((source.Value, lop.Value)))
                {
                    retryId = default;
                    reason = "409 conflict — retry_item_claims overlap";
                    return false;
                }
            }

            retryId = RequestId.New();
            foreach (var lop in logicalIds)
                _claims.Add((source.Value, lop.Value));
            _idempotency[idempotencyKey] = retryId;
            reason = "claimed";
            return true;
        }
    }
}
