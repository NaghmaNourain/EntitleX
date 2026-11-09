// =============================================================================
// EntitleX Educational Guide 11 — Distributed Systems Patterns in .NET
// File: docs/guides/11_DistributedSystemsInDotNet.cs
// =============================================================================
// Standalone compilable demos. Each DemoXX maps a classic distributed-systems
// pattern onto a concrete EntitleX mechanism (Polly, outbox, saga, Kafka, ...).
//
// Run (optional):
//   dotnet new console -n Guide11 -o /tmp/Guide11 --force
//   cp docs/guides/11_DistributedSystemsInDotNet.cs /tmp/Guide11/Program.cs
//   dotnet run --project /tmp/Guide11
//
// Real paths:
//   src/EntitleX.Infrastructure/Providers/ProviderHttpClientRegistration.cs
//   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
//   src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
//   src/EntitleX.Infrastructure/Retry/EfRetryClaimStore.cs
//   src/EntitleX.Messaging/Kafka/KafkaMessagePublisher.cs
//   docs/{system-design,idempotency,saga-workflow,event-contracts,failure-scenarios}.md
// =============================================================================

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// #############################################################################
// TABLE OF CONTENTS
// #############################################################################
//  Demo01  Retry
//  Demo02  Timeouts
//  Demo03  CircuitBreaker
//  Demo04  Bulkhead
//  Demo05  Idempotency
//  Demo06  OutboxInbox
//  Demo07  SagaOrchestrator (forward recovery, not 2PC)
//  Demo08  LeaseFencing (outbox lock + retry claims)
//  Demo09  LeaderElectionLight (why SKIP LOCKED instead)
//  Demo10  ConsistentHashingKafkaPartition
//  Demo11  RateLimiter
//  Demo12  OptimisticConcurrency
//  Demo13  ClocksUtc
//  Demo14  QuorumLight
//  Demo15  WhyNotClassic2Pc
//  Demo16  SingleFlightReconProbes
//  Demo17  Backpressure
//  Demo18  ResiliencePipeline
//  Demo19  PartitionedLogKafkaTopics
//  Demo20  PatternToEntitleXMap
// #############################################################################

namespace EntitleX.Guides.DistributedSystems;

internal static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX Guide 11: Distributed Systems in .NET ===\n");
        await Demo01_Retry.RunAsync();
        await Demo02_Timeouts.RunAsync();
        await Demo03_CircuitBreaker.RunAsync();
        await Demo04_Bulkhead.RunAsync();
        Demo05_Idempotency.Run();
        Demo06_OutboxInbox.Run();
        Demo07_SagaOrchestrator.Run();
        Demo08_LeaseFencing.Run();
        Demo09_LeaderElectionLight.Run();
        Demo10_ConsistentHashingKafkaPartition.Run();
        await Demo11_RateLimiter.RunAsync();
        Demo12_OptimisticConcurrency.Run();
        Demo13_ClocksUtc.Run();
        Demo14_QuorumLight.Run();
        Demo15_WhyNotClassic2Pc.Run();
        await Demo16_SingleFlightReconProbes.RunAsync();
        await Demo17_Backpressure.RunAsync();
        await Demo18_ResiliencePipeline.RunAsync();
        Demo19_PartitionedLogKafkaTopics.Run();
        Demo20_PatternToEntitleXMap.Run();
        Console.WriteLine("\nGuide 11 complete.");
    }
}

// Shared tiny helpers for demos ------------------------------------------------
public sealed class TransientException : Exception
{
    public TransientException(string m) : base(m) { }
}

public static class DemoClock
{
    // EntitleX rule: always DateTimeOffset.UtcNow for business/lease/saga times.
    public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

// =============================================================================
// Demo01 Retry — Polly GET retries; POST commands NO retry
// =============================================================================
// Real: ProviderHttpClientRegistration.AddIdempotentGetClient vs AddCommandClient
// Docs: system-design.md "Provider HTTP resilience (Polly)"
public static class Demo01_Retry
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo01 Retry --");
        Console.WriteLine("  GET discovery/status: exponential retry + jitter (safe).");
        Console.WriteLine("  POST license-extension commands: NO retry (uncertain effect).");

        var attempts = 0;
        async Task<string> IdempotentGetAsync()
        {
            attempts++;
            if (attempts < 3) throw new TransientException("CloudShield GET 503");
            return "ok";
        }

        // Mini retry loop mimicking Polly HttpRetryStrategyOptions on GET clients
        string? result = null;
        for (var i = 0; i < 5; i++)
        {
            try
            {
                result = await IdempotentGetAsync();
                break;
            }
            catch (TransientException) when (i < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * Math.Pow(2, i)));
            }
        }
        Console.WriteLine($"  GET succeeded after {attempts} attempts: {result}");

        // Command path: fail once and do NOT retry — recon will probe later.
        try
        {
            throw new TransientException("NetworkGuard POST 502 after accept?");
        }
        catch (TransientException ex)
        {
            Console.WriteLine($"  POST failed once → leave Uncertain, do not re-POST: {ex.Message}");
        }
        Console.WriteLine("  Path: ProviderHttpClientRegistration.cs\n");
    }
}

// =============================================================================
// Demo02 Timeouts — Polly owns timeouts; HttpClient.Timeout = Infinite
// =============================================================================
public static class Demo02_Timeouts
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo02 Timeouts --");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  Provider call cancelled by timeout strategy (demo).");
        }
        Console.WriteLine("  Saga timeouts: ApprovalPending → Escalated;");
        Console.WriteLine("                 FulfillmentProcessing → Uncertain + ReconciliationRequired");
        Console.WriteLine("  Real: LicenseExtensionSaga.ApplyTimeout / ISagaTimeoutHook\n");
    }
}

// =============================================================================
// Demo03 CircuitBreaker — opens on provider failure ratio
// =============================================================================
// Real: HttpCircuitBreakerStrategyOptions + EntitleXMeters.CircuitBreakerTransitions
public sealed class MiniCircuitBreaker
{
    private readonly int _minimumThroughput;
    private readonly double _failureRatio;
    private readonly TimeSpan _breakDuration;
    private int _successes;
    private int _failures;
    private DateTimeOffset? _openedUntil;

    public MiniCircuitBreaker(int minimumThroughput, double failureRatio, TimeSpan breakDuration)
    {
        _minimumThroughput = minimumThroughput;
        _failureRatio = failureRatio;
        _breakDuration = breakDuration;
    }

    public string State =>
        _openedUntil is { } until && until > DemoClock.UtcNow ? "Open" : "Closed";

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        if (State == "Open")
            throw new InvalidOperationException("Circuit open — fail fast (CloudShield)");

        try
        {
            var result = await action();
            _successes++;
            return result;
        }
        catch
        {
            _failures++;
            var total = _successes + _failures;
            if (total >= _minimumThroughput && (double)_failures / total >= _failureRatio)
                _openedUntil = DemoClock.UtcNow.Add(_breakDuration);
            throw;
        }
    }
}

public static class Demo03_CircuitBreaker
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo03 CircuitBreaker --");
        var cb = new MiniCircuitBreaker(minimumThroughput: 4, failureRatio: 0.5,
            breakDuration: TimeSpan.FromMilliseconds(100));

        for (var i = 0; i < 4; i++)
        {
            try
            {
                await cb.ExecuteAsync<string>(async () =>
                {
                    await Task.Yield();
                    throw new TransientException("boom");
                });
            }
            catch { /* expected */ }
        }
        Console.WriteLine($"  After failures: state={cb.State}");
        try { await cb.ExecuteAsync(() => Task.FromResult("x")); }
        catch (InvalidOperationException ex) { Console.WriteLine($"  {ex.Message}"); }
        Console.WriteLine("  Metric: entitlex_circuit_breaker_transitions_total{provider,state}\n");
    }
}

// =============================================================================
// Demo04 Bulkhead — concurrency limiter per provider client
// =============================================================================
// Real: builder.AddConcurrencyLimiter(BulkheadMaxParallelization, BulkheadQueueLimit)
public static class Demo04_Bulkhead
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo04 Bulkhead --");
        using var gate = new SemaphoreSlim(2, 2); // max parallelization = 2
        var running = 0;
        var maxObserved = 0;

        async Task Work(int id)
        {
            if (!await gate.WaitAsync(TimeSpan.FromMilliseconds(100)))
            {
                Console.WriteLine($"  task {id} shed (bulkhead queue full)");
                return;
            }
            try
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref maxObserved, now);
                await Task.Delay(30);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(1, 6).Select(Work));
        Console.WriteLine($"  Max concurrent provider calls observed: {maxObserved} (cap 2)");
        Console.WriteLine("  Isolates CloudShield storms from NetworkGuard clients.\n");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = target;
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}

// =============================================================================
// Demo05 Idempotency — api_idempotency_records, processed_messages, AppliedOps
// =============================================================================
public static class Demo05_Idempotency
{
    public static void Run()
    {
        Console.WriteLine("-- Demo05 Idempotency --");
        var processed = new HashSet<(string Consumer, string Key)>();
        var apiKeys = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var appliedOps = new HashSet<Guid>();

        // Consumer inbox
        bool TryProcess(string consumer, string key)
        {
            if (!processed.Add((consumer, key))) return false; // duplicate
            return true;
        }
        Console.WriteLine($"  first consume: {TryProcess("entitlex-provider-results", "msg-1")}");
        Console.WriteLine($"  replay consume: {TryProcess("entitlex-provider-results", "msg-1")}");

        // HTTP selective retry idempotency
        Guid StartRetry(string idempotencyKey)
        {
            if (apiKeys.TryGetValue(idempotencyKey, out var existing)) return existing;
            var retryId = Guid.NewGuid();
            apiKeys[idempotencyKey] = retryId;
            return retryId;
        }
        var a = StartRetry("client-key-42");
        var b = StartRetry("client-key-42");
        Console.WriteLine($"  lost-response replay same retry id? {a == b}");

        // Provider unique logical_operation_id
        bool Apply(Guid logical)
        {
            if (!appliedOps.Add(logical))
            {
                Console.WriteLine("  AppliedLicenseOperation replay → no-op success");
                return true;
            }
            return true;
        }
        var logical = Guid.NewGuid();
        Apply(logical);
        Apply(logical);
        Console.WriteLine("  Tables: api_idempotency_records, processed_messages,");
        Console.WriteLine("          applied_license_operations, retry_item_claims\n");
    }
}

// =============================================================================
// Demo06 Outbox / Inbox
// =============================================================================
public enum OutboxStatus { Pending, Processing, Published, Poison }

public sealed class OutboxRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public string Destination { get; init; } = "entitlex-fulfillment";
    public string Payload { get; init; } = "{}";
}

public static class Demo06_OutboxInbox
{
    public static void Run()
    {
        Console.WriteLine("-- Demo06 Outbox / Inbox --");
        // Same PG transaction as business change → outbox Pending
        var outbox = new List<OutboxRow>
        {
            new() { Payload = """{"messageType":"entitlex.fulfillment.command"}""" }
        };
        Console.WriteLine($"  Business + outbox committed together. rows={outbox.Count}");

        // Publisher claims (see Demo08 for SKIP LOCKED details)
        var row = outbox[0];
        row.Status = OutboxStatus.Processing;
        row.Attempts++;
        // publish SQS ...
        row.Status = OutboxStatus.Published;
        Console.WriteLine($"  Published to SQS; status={row.Status}");

        // Inbox = processed_messages at consumer
        Console.WriteLine("  Inbox key: (consumer_name, idempotency_key)");
        Console.WriteLine("  Real: OutboxWriter + OutboxClaimer + EfConsumerIdempotencyStore\n");
    }
}

// =============================================================================
// Demo07 Saga orchestrator — forward recovery, NOT compensation/2PC
// =============================================================================
public static class Demo07_SagaOrchestrator
{
    public enum State { Approved, Queued, Processing, PartiallyCompleted, Completed, Failed }

    public static void Run()
    {
        Console.WriteLine("-- Demo07 Saga orchestrator (forward recovery) --");
        var state = State.Approved;
        state = State.Queued;      // write outbox + seed Pending outcomes
        state = State.Processing;  // Kafka commands dispatched

        var outcomes = new Dictionary<string, string>
        {
            ["L1"] = "Succeeded", // CloudShield applied
            ["L2"] = "Failed"     // NetworkGuard failed
        };

        state = outcomes.Values.Distinct().Count() > 1
            ? State.PartiallyCompleted
            : outcomes.ContainsValue("Failed") ? State.Failed : State.Completed;

        Console.WriteLine($"  Mixed outcomes → {state}");
        Console.WriteLine("  Selective retry copies failed L2 only; L1 untouched.");
        Console.WriteLine("  There is NO compensating 'shorten expiry' transaction.");
        Console.WriteLine("  Real: LicenseExtensionSaga.RecordItem* / AggregateStatus\n");
    }
}

// =============================================================================
// Demo08 Lease / fencing — outbox lock + retry_item_claims
// =============================================================================
public static class Demo08_LeaseFencing
{
    public static void Run()
    {
        Console.WriteLine("-- Demo08 Lease / fencing --");
        var lockedBy = "outbox-publisher-a";
        var lockedUntil = DemoClock.UtcNow.AddSeconds(30);
        Console.WriteLine($"  Outbox lease: locked_by={lockedBy} until={lockedUntil:o}");
        Console.WriteLine("  Stale reclaim: status=Processing AND locked_until_utc < now()");
        Console.WriteLine("  SQL: FOR UPDATE SKIP LOCKED (OutboxClaimer.cs)");

        // Retry claims as fencing for concurrent selective retries
        var claims = new HashSet<(Guid Source, Guid Logical)>();
        var source = Guid.NewGuid();
        var logical = Guid.NewGuid();
        Console.WriteLine($"  claim1: {claims.Add((source, logical))}");
        Console.WriteLine($"  claim2 concurrent: {claims.Add((source, logical))} → 409 Conflict in API");
        Console.WriteLine("  Real: retry_item_claims PK + EfRetryClaimStore.TryClaimItemsAsync\n");
    }
}

// =============================================================================
// Demo09 Leader election (light) — why workers use SKIP LOCKED instead
// =============================================================================
public static class Demo09_LeaderElectionLight
{
    public static void Run()
    {
        Console.WriteLine("-- Demo09 Leader election (light) --");
        Console.WriteLine("""
          Classic leader election (etcd/ZooKeeper/SQL advisory lock):
            one active OutboxPublisher, others standby.

          EntitleX choice: cooperative multi-worker claiming with SKIP LOCKED.
            + No single leader failover pause
            + Horizontal scale of publishers
            + Lease via locked_until_utc still fences dead workers
            − Requires idempotent publish + poison handling

          When would we elect a leader? Coordinating singleton schedules that
          must not overlap (some reconciliation topologies) — EntitleX still
          prefers reclaimable leases / SKIP LOCKED for outbox throughput.
        """);
    }
}

// =============================================================================
// Demo10 Consistent hashing / Kafka partition by entitlement id
// =============================================================================
public static class Demo10_ConsistentHashingKafkaPartition
{
    public static void Run()
    {
        Console.WriteLine("-- Demo10 Kafka partition by entitlement id --");
        // Real: KafkaMessagePublisher Key = partitionKey ?? messageId
        // FulfillmentWorker publishes provider commands with key = entitlement id
        // → per-entitlement ordering on cloudshield/networkguard command topics.

        int Partition(string entitlementId, int partitionCount)
        {
            // Simplified hash; Kafka uses its own murmur2 for keys.
            unchecked
            {
                var hash = entitlementId.Aggregate(0, (a, c) => (a * 31) + c);
                return Math.Abs(hash) % partitionCount;
            }
        }

        var partitions = 6;
        var keys = new[] { "ent-cs-1", "ent-cs-1", "ent-cs-2", "lic-ng-9" };
        foreach (var k in keys)
            Console.WriteLine($"  key={k} → partition {Partition(k, partitions)}");
        Console.WriteLine("  Same entitlement always same partition → ordered apply.\n");
    }
}

// =============================================================================
// Demo11 Rate limiter — protect simulators / shared deps
// =============================================================================
public static class Demo11_RateLimiter
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo11 Rate limiter --");
        // Token bucket sketch (Polly rate limiter / concurrency limiter sibling)
        var tokens = 3;
        for (var i = 1; i <= 5; i++)
        {
            if (tokens > 0)
            {
                tokens--;
                Console.WriteLine($"  call {i}: allowed (tokens left={tokens})");
            }
            else Console.WriteLine($"  call {i}: rate-limited (429 / shed)");
            await Task.Yield();
        }
        Console.WriteLine("  Compose with bulkhead in ProviderHttpClientRegistration.\n");
    }
}

// =============================================================================
// Demo12 Optimistic concurrency — row_version
// =============================================================================
public static class Demo12_OptimisticConcurrency
{
    public static void Run()
    {
        Console.WriteLine("-- Demo12 Optimistic concurrency --");
        var rowVersion = 3u;
        uint Read() => rowVersion;
        bool TryWrite(uint expected, Action mutate)
        {
            if (expected != rowVersion) return false;
            mutate();
            rowVersion++;
            return true;
        }

        var a = Read();
        var b = Read();
        Console.WriteLine($"  writer A wins: {TryWrite(a, () => { })} → version={rowVersion}");
        Console.WriteLine($"  writer B stale: {TryWrite(b, () => { })} → DbUpdateConcurrencyException in EF");
        Console.WriteLine("  Column: license_extension_requests.row_version\n");
    }
}

// =============================================================================
// Demo13 Clocks — DateTimeOffset Utc
// =============================================================================
public static class Demo13_ClocksUtc
{
    public static void Run()
    {
        Console.WriteLine("-- Demo13 Clocks (DateTimeOffset Utc) --");
        var created = DateTimeOffset.UtcNow;
        var deadline = created.AddDays(3); // ApprovalPending timeout
        Console.WriteLine($"  created={created:o}");
        Console.WriteLine($"  approval deadline={deadline:o}");
        Console.WriteLine("  Never DateTime.Now for saga leases / outbox available_at_utc.");
        Console.WriteLine("  DB type: timestamptz everywhere (guide 09).\n");
    }
}

// =============================================================================
// Demo14 Quorum (light) — what EntitleX does / doesn't need
// =============================================================================
public static class Demo14_QuorumLight
{
    public static void Run()
    {
        Console.WriteLine("-- Demo14 Quorum (light) --");
        Console.WriteLine("""
          Quorum (R+W>N) matters for multi-replica datastores / consensus logs.
          EntitleX local Compose uses single-node Postgres + Kafka — no app-level
          quorum reads. Interview mapping:
            * Kafka ISR replication is the broker's quorum story (ops concern).
            * Control-plane truth is the platform DB row (saga outcomes).
            * Provider truth is AppliedLicenseOperation; recon GETs reconcile them.
          Do not invent quorum protocols for selective retry — use unique indexes.
        """);
    }
}

// =============================================================================
// Demo15 Why NOT classic 2PC
// =============================================================================
public static class Demo15_WhyNotClassic2Pc
{
    public static void Run()
    {
        Console.WriteLine("-- Demo15 Why NOT classic 2PC --");
        Console.WriteLine("""
          Classic 2PC across EntitleX.Api + CloudShield + NetworkGuard would:
            * Hold locks while remote vendors decide (availability disaster)
            * Require a prepare/commit API vendors do not offer
            * Still fail on coordinator crash (need presumed-abort etc.)

          EntitleX instead:
            1) Local ACID on entitlex_platform (request/saga/outbox)
            2) At-least-once messaging (SQS/Kafka)
            3) Idempotent provider apply (unique logical_operation_id)
            4) Forward recovery saga + selective retry + reconciliation probes

          Compensation is also rejected for product reasons (cannot safely
          shorten a customer's already-granted expiry). See docs/saga-workflow.md
        """);
    }
}

// =============================================================================
// Demo16 SingleFlight — coalesce concurrent recon probes
// =============================================================================
public sealed class SingleFlight<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Task<TValue>> _inflight = new();

    public async Task<TValue> DoAsync(TKey key, Func<Task<TValue>> factory)
    {
        var task = _inflight.GetOrAdd(key, _ => factory());
        try { return await task; }
        finally { _inflight.TryRemove(key, out var _); }
    }
}

public static class Demo16_SingleFlightReconProbes
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo16 SingleFlight for recon probes --");
        var flight = new SingleFlight<Guid, string>();
        var logical = Guid.NewGuid();
        var calls = 0;

        async Task<string> ProbeAsync()
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(40); // HTTP GET provider by LogicalOperationId
            return "Applied";
        }

        var results = await Task.WhenAll(
            flight.DoAsync(logical, ProbeAsync),
            flight.DoAsync(logical, ProbeAsync),
            flight.DoAsync(logical, ProbeAsync));

        Console.WriteLine($"  concurrent recon callers=3, provider GETs={calls}, status={results[0]}");
        Console.WriteLine("  Avoid stampedes when ReconciliationWorker + manual admin overlap.\n");
    }
}

// =============================================================================
// Demo17 Backpressure — SQS depth / outbox backlog / bulkhead
// =============================================================================
public static class Demo17_Backpressure
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo17 Backpressure --");
        var outboxBacklog = 1200;
        var sqsDepth = 800;
        Console.WriteLine($"  gauges: entitlex_outbox_backlog={outboxBacklog}");
        Console.WriteLine($"          entitlex_sqs_approximate_depth={sqsDepth}");

        // Slow down producers when backlog high (token delay sketch)
        var delayMs = outboxBacklog > 1000 ? 100 : 0;
        if (delayMs > 0)
        {
            await Task.Delay(delayMs);
            Console.WriteLine($"  applied producer delay {delayMs}ms (backpressure)");
        }
        Console.WriteLine("  Workers: HttpListener metrics :9180–9183; SqsDepthProbe\n");
    }
}

// =============================================================================
// Demo18 Resilience pipeline — compose timeout + retry + CB + bulkhead
// =============================================================================
public static class Demo18_ResiliencePipeline
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo18 Resilience pipeline --");
        Console.WriteLine("  GET pipeline:  Timeout → Retry(GET-only) → CircuitBreaker → Bulkhead");
        Console.WriteLine("  POST pipeline: Timeout → CircuitBreaker → Bulkhead  (no Retry)");

        async Task<string> PipelineGet(Func<Task<string>> inner)
        {
            // Order matches typical Polly AddResilienceHandler registration
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            // bulkhead omitted for brevity — see Demo04
            return await inner();
        }

        var body = await PipelineGet(() => Task.FromResult("CloudShield entitlements[]"));
        Console.WriteLine($"  sample GET result: {body}");
        Console.WriteLine("  Real composition: ProviderHttpClientRegistration.cs\n");
    }
}

// =============================================================================
// Demo19 Partitioned log — Kafka topics topology
// =============================================================================
public static class Demo19_PartitionedLogKafkaTopics
{
    public static void Run()
    {
        Console.WriteLine("-- Demo19 Partitioned log (Kafka topics) --");
        Console.WriteLine("""
          Topics (docs/event-contracts.md):
            cloudshield.license-extension.commands   (key=entitlement id)
            networkguard.license-extension.commands  (key=entitlement id)
            entitlex.provider.results                (saga settlement)

          Why SQS then Kafka?
            SQS = control-plane work queue + DLQ (maxReceiveCount 5) after outbox
            Kafka = ordered per-entitlement fan-out to vendor simulators + results

          Consumer groups:
            entitlex-fulfillment / entitlex-orchestrator-results
            cloudshield-commands / networkguard-commands

          At-least-once ⇒ processed_messages inbox + unique LogicalOperationId
        """);

        Console.WriteLine("  Interview closer: messaging is a partitioned log + work queue,");
        Console.WriteLine("  not a distributed transaction coordinator.");
    }
}


// =============================================================================
// Demo20 Pattern → EntitleX map (cheat sheet)
// =============================================================================
public static class Demo20_PatternToEntitleXMap
{
    public static void Run()
    {
        Console.WriteLine("-- Demo20 Pattern → EntitleX map --");
        var map = new (string Pattern, string EntitleX)[]
        {
            ("Retry", "Polly GET only — ProviderHttpClientRegistration"),
            ("Timeout", "Polly TimeoutStrategy + saga ApprovalPending/FulfillmentProcessing"),
            ("Circuit breaker", "HttpCircuitBreakerStrategyOptions + meters"),
            ("Bulkhead", "AddConcurrencyLimiter per named HttpClient"),
            ("Idempotency", "api_idempotency_records + processed_messages + unique logical ids"),
            ("Outbox/Inbox", "outbox_messages + processed_messages"),
            ("Saga", "LicenseExtensionSaga forward recovery (no compensate)"),
            ("Lease/fencing", "outbox locked_until_utc/locked_by + retry_item_claims"),
            ("Leader election", "avoided for outbox — SKIP LOCKED multi-worker"),
            ("Consistent hash", "Kafka key = entitlement id"),
            ("Rate limit", "compose with bulkhead / token bucket at edge"),
            ("Optimistic concurrency", "row_version on license_extension_requests"),
            ("Clocks", "DateTimeOffset.UtcNow / timestamptz"),
            ("Quorum", "broker ISR concern; app uses unique constraints"),
            ("2PC", "rejected — local ACID + messaging + recon"),
            ("SingleFlight", "coalesce recon GETs by LogicalOperationId"),
            ("Backpressure", "outbox/SQS depth gauges + bulkhead shedding"),
            ("Resilience pipeline", "GET vs POST Polly handler graphs"),
            ("Partitioned log", "Kafka command/results topics"),
        };
        foreach (var (pattern, entitlex) in map)
            Console.WriteLine($"  {pattern,-22} → {entitlex}");
    }
}

// END guide 11
