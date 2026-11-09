// =============================================================================
// EntitleX Educational Guide 06 — Low-Level Design (LLD) Complete Guide
// =============================================================================
//
// HOW TO USE THIS FILE
// --------------------
// Standalone teaching artifact. Inline types compile without referencing real
// projects. Prefer deep EntitleX designs over unrelated toys (TicTacToe, etc.).
//
// REAL REPO ANCHORS
// -----------------
//   src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
//   src/EntitleX.Domain/Approval/ApprovalCase.cs
//   src/EntitleX.Domain/Approval/ApprovalRules.cs
//   src/EntitleX.Domain/LicenseExtension/FailedItemSelector.cs
//   src/EntitleX.Infrastructure/Retry/ApiIdempotencyRecord.cs
//   src/EntitleX.Infrastructure/Retry/EfRetryClaimStore.cs
//   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
//   src/EntitleX.Infrastructure/Providers/ProviderOptions.cs
//   src/EntitleX.Infrastructure/Providers/ProviderHttpClientRegistration.cs
//   docs/saga-workflow.md
//   docs/idempotency.md
//   docs/failure-scenarios.md
//
// TABLE OF CONTENTS
// -----------------
//   PART 0  — Domain stubs + DemoHost
//   PART 1  — OOP building blocks (entities, VOs, aggregates)
//   PART 2  — SOLID applied to EntitleX
//   PART 3  — Creational patterns
//   PART 4  — Structural patterns
//   PART 5  — Behavioral patterns
//   PART 6  — Concurrency LLD (leases, claims, optimistic concurrency)
//   PART 7  — Classic problem: Approval workflow state machine
//   PART 8  — Classic problem: Idempotency store (LRU + TTL)
//   PART 9  — Classic problem: Rate limiter / bulkhead for provider HTTP
//   PART 10 — Classic problem: Outbox claim lease
//   PART 11 — Classic problem: Saga orchestrator (forward recovery)
//   PART 12 — Classic problem: Notification fan-out (strategy)
//   PART 13 — Classic problem: Provider adapter (adapter + strategy)
//   PART 14 — Classic problem: Selective retry lineage
//   PART 15 — Toy mappings (elevator/splitwise → EntitleX analogies)
//   PART 16 — Interview checklist
//
// =============================================================================

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EntitleX.Guides.LLD
{

#region PART 0 — Domain stubs

namespace Domain
{
    public enum ApprovalBand
    {
        AutoApprove,
        RenewalManagerOnly,
        RenewalManagerThenDealDesk,
        Unsupported
    }

    public enum SagaState
    {
        Requested,
        PendingRenewalManagerApproval,
        PendingDealDeskApproval,
        Approved,
        QueuedForFulfillment,
        Processing,
        PartiallyCompleted,
        Completed,
        Failed,
        ReconciliationRequired,
        Escalated,
        Rejected
    }

    public enum ItemOutcomeStatus { Pending, Succeeded, Failed }

    public enum ProviderSystem { CloudShield, NetworkGuard }

    public static class Roles
    {
        public const string RenewalRepresentative = "RenewalRepresentative";
        public const string RenewalManager = "RenewalManager";
        public const string DealDeskManager = "DealDeskManager";
        public const string PlatformAdmin = "PlatformAdmin";
    }

    public sealed record LogicalOperationId(Guid Value)
    {
        public static LogicalOperationId New() => new(Guid.NewGuid());
        public override string ToString() => Value.ToString("D");
    }

    public sealed record AttemptId(Guid Value)
    {
        public static AttemptId New() => new(Guid.NewGuid());
    }

    public sealed class ExtensionItem
    {
        public LogicalOperationId LogicalOperationId { get; init; } = LogicalOperationId.New();
        public AttemptId AttemptId { get; init; } = AttemptId.New();
        public AttemptId? ParentId { get; init; }
        public string EntitlementId { get; init; } = "";
        public ProviderSystem Provider { get; init; }
        public int ExtensionDays { get; init; }
        public ItemOutcomeStatus Status { get; set; } = ItemOutcomeStatus.Pending;
    }

    public static class ApprovalRules
    {
        public static ApprovalBand Evaluate(int maxDays) => maxDays switch
        {
            < 1 => throw new ArgumentOutOfRangeException(nameof(maxDays)),
            <= 30 => ApprovalBand.AutoApprove,
            <= 90 => ApprovalBand.RenewalManagerOnly,
            <= 365 => ApprovalBand.RenewalManagerThenDealDesk,
            _ => ApprovalBand.Unsupported
        };
    }
}

#endregion

public static class DemoHost
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX LLD Complete Guide ===\n");
        Part1_OOP.Run();
        Part2_SOLID.Run();
        Part3_Creational.Run();
        Part4_Structural.Run();
        Part5_Behavioral.Run();
        await Part6_Concurrency.RunAsync();
        Part7_ApprovalStateMachine.Run();
        Part8_IdempotencyStore.Run();
        await Part9_RateLimiter.RunAsync();
        await Part10_OutboxLease.RunAsync();
        Part11_SagaOrchestrator.Run();
        Part12_NotificationFanOut.Run();
        Part13_ProviderAdapter.Run();
        Part14_SelectiveRetryLineage.Run();
        Part15_ToyMappings.Run();
        Part16_Interview.Print();
    }
}

// =============================================================================
// PART 1 — OOP
// =============================================================================
/// <summary>
/// Aggregate roots in EntitleX: LicenseExtensionRequest, ApprovalCase,
/// LicenseExtensionSaga. Value objects: LogicalOperationId, AttemptId, bands.
/// </summary>
public static class Part1_OOP
{
    public static void Run()
    {
        Console.WriteLine("-- PART 1: OOP --");

        // Entity identity vs value equality
        var a = Domain.LogicalOperationId.New();
        var b = new Domain.LogicalOperationId(a.Value);
        Console.WriteLine($"  LogicalOperationId value-equal? {a == b}");

        // Aggregate invariant: band derived from MAX(item.ExtensionDays)
        var items = new List<Domain.ExtensionItem>
        {
            new() { EntitlementId = "CS-1", Provider = Domain.ProviderSystem.CloudShield, ExtensionDays = 14 },
            new() { EntitlementId = "NG-1", Provider = Domain.ProviderSystem.NetworkGuard, ExtensionDays = 120 },
        };
        var band = Domain.ApprovalRules.Evaluate(items.Max(i => i.ExtensionDays));
        Console.WriteLine($"  aggregate band={band} (DealDesk required)");

        // Encapsulation: saga exposes RecordRenewalManagerApproval — not raw State set
        var saga = new MiniSaga(band);
        saga.Start();
        Console.WriteLine($"  saga after start: {saga.State}");
        Console.WriteLine();
    }

    /// <summary>Tiny aggregate teaching encapsulation (see LicenseExtensionSaga).</summary>
    public sealed class MiniSaga
    {
        private readonly Domain.ApprovalBand _band;
        public Domain.SagaState State { get; private set; } = Domain.SagaState.Requested;
        public MiniSaga(Domain.ApprovalBand band) => _band = band;
        public void Start()
        {
            State = _band switch
            {
                Domain.ApprovalBand.AutoApprove => Domain.SagaState.Approved,
                Domain.ApprovalBand.Unsupported => Domain.SagaState.Rejected,
                Domain.ApprovalBand.RenewalManagerOnly => Domain.SagaState.PendingRenewalManagerApproval,
                _ => Domain.SagaState.PendingRenewalManagerApproval
            };
        }
    }
}

// =============================================================================
// PART 2 — SOLID
// =============================================================================
public static class Part2_SOLID
{
    public static void Run()
    {
        Console.WriteLine("-- PART 2: SOLID --");
        Console.WriteLine("""
  S — ApprovalRules only evaluates bands; Saga only orchestrates transitions.
  O — Add a new ProviderSystem via IProviderAdapter without editing orchestrator.
  L — CloudShieldAdapter and NetworkGuardAdapter both satisfy IProviderAdapter.
  I — IOutboxWriter ≠ IOutboxClaimer ≠ IConsumerIdempotencyStore (ISP).
  D — Application depends on IUnitOfWork / ISender abstractions; Infrastructure
      supplies EntitleXDbContext and MediatR handlers.
""");
        // Demo OCP: strategy map
        IBandNotifier notifier = new RoleBasedNotifier();
        notifier.Notify(Domain.ApprovalBand.RenewalManagerThenDealDesk);
        Console.WriteLine();
    }

    public interface IBandNotifier
    {
        void Notify(Domain.ApprovalBand band);
    }

    public sealed class RoleBasedNotifier : IBandNotifier
    {
        public void Notify(Domain.ApprovalBand band)
        {
            var roles = band switch
            {
                Domain.ApprovalBand.RenewalManagerOnly => new[] { Domain.Roles.RenewalManager },
                Domain.ApprovalBand.RenewalManagerThenDealDesk =>
                    new[] { Domain.Roles.RenewalManager, Domain.Roles.DealDeskManager },
                _ => Array.Empty<string>()
            };
            Console.WriteLine("  notify roles: " + string.Join(",", roles));
        }
    }
}

// =============================================================================
// PART 3 — Creational patterns
// =============================================================================
public static class Part3_Creational
{
    public static void Run()
    {
        Console.WriteLine("-- PART 3: Creational --");

        // Factory: create saga seeded from band (LicenseExtensionSaga.Create…)
        var saga = SagaFactory.Create(Domain.ApprovalBand.RenewalManagerOnly);
        Console.WriteLine($"  factory → {saga.State}");

        // Builder: fulfillment command envelope
        var cmd = new FulfillmentCommandBuilder()
            .WithLogical(Domain.LogicalOperationId.New())
            .WithAttempt(Domain.AttemptId.New())
            .WithProvider(Domain.ProviderSystem.CloudShield)
            .WithEntitlement("CS-ENT-001")
            .Build();
        Console.WriteLine($"  builder command provider={cmd.Provider}");

        // Singleton (careful): process-wide metrics meter — not DbContext!
        var meters = OutboxMetrics.Instance;
        meters.IncrementClaimed(3);
        Console.WriteLine($"  singleton claimed={meters.Claimed}");

        // Object pool analogy: Npgsql connection pool (Max Pool Size=100)
        Console.WriteLine("  (connection pool is infrastructure — do not hand-roll)");
        Console.WriteLine();
    }

    public static class SagaFactory
    {
        public static Part1_OOP.MiniSaga Create(Domain.ApprovalBand band)
        {
            var s = new Part1_OOP.MiniSaga(band);
            s.Start();
            return s;
        }
    }

    public sealed record FulfillmentCommand(
        Domain.LogicalOperationId LogicalOperationId,
        Domain.AttemptId AttemptId,
        Domain.ProviderSystem Provider,
        string EntitlementId);

    public sealed class FulfillmentCommandBuilder
    {
        private Domain.LogicalOperationId? _logical;
        private Domain.AttemptId? _attempt;
        private Domain.ProviderSystem _provider;
        private string _entitlement = "";
        public FulfillmentCommandBuilder WithLogical(Domain.LogicalOperationId id) { _logical = id; return this; }
        public FulfillmentCommandBuilder WithAttempt(Domain.AttemptId id) { _attempt = id; return this; }
        public FulfillmentCommandBuilder WithProvider(Domain.ProviderSystem p) { _provider = p; return this; }
        public FulfillmentCommandBuilder WithEntitlement(string e) { _entitlement = e; return this; }
        public FulfillmentCommand Build() =>
            new(_logical ?? throw new InvalidOperationException(),
                _attempt ?? throw new InvalidOperationException(),
                _provider, _entitlement);
    }

    public sealed class OutboxMetrics
    {
        public static OutboxMetrics Instance { get; } = new();
        private OutboxMetrics() { }
        public int Claimed { get; private set; }
        public void IncrementClaimed(int n) => Claimed += n;
    }
}

// =============================================================================
// PART 4 — Structural patterns
// =============================================================================
public static class Part4_Structural
{
    public static void Run()
    {
        Console.WriteLine("-- PART 4: Structural --");

        // Adapter: unify CloudShield / NetworkGuard HTTP shapes
        IProviderAdapter cs = new CloudShieldAdapter();
        IProviderAdapter ng = new NetworkGuardAdapter();
        Console.WriteLine($"  adapters: {cs.Name}, {ng.Name}");

        // Facade: Application service that hides saga + outbox + approval
        var facade = new LicenseExtensionFacade();
        facade.Submit(maxDays: 45);
        facade.Submit(maxDays: 15);

        // Decorator: Polly-like retry wrapper around adapter
        IProviderAdapter decorated = new RetryingProviderDecorator(cs, maxAttempts: 3);
        decorated.Extend("CS-ENT-001");

        // Proxy: reconciliation gate — block fulfill if ReconciliationRequired
        var gate = new ReconciliationGateProxy(allowFulfill: false);
        Console.WriteLine($"  fulfill allowed? {gate.TryFulfill()}");
        Console.WriteLine();
    }

    public interface IProviderAdapter
    {
        string Name { get; }
        void Extend(string entitlementId);
    }

    public sealed class CloudShieldAdapter : IProviderAdapter
    {
        public string Name => "CloudShield";
        public void Extend(string entitlementId) =>
            Console.WriteLine($"  CloudShield.extend({entitlementId})");
    }

    public sealed class NetworkGuardAdapter : IProviderAdapter
    {
        public string Name => "NetworkGuard";
        public void Extend(string entitlementId) =>
            Console.WriteLine($"  NetworkGuard.extend({entitlementId})");
    }

    public sealed class RetryingProviderDecorator : IProviderAdapter
    {
        private readonly IProviderAdapter _inner;
        private readonly int _maxAttempts;
        public RetryingProviderDecorator(IProviderAdapter inner, int maxAttempts)
        {
            _inner = inner; _maxAttempts = maxAttempts;
        }
        public string Name => _inner.Name + "+Retry";
        public void Extend(string entitlementId)
        {
            for (int i = 1; i <= _maxAttempts; i++)
            {
                try { _inner.Extend(entitlementId); return; }
                catch when (i < _maxAttempts) { /* backoff */ }
            }
        }
    }

    public sealed class LicenseExtensionFacade
    {
        public void Submit(int maxDays)
        {
            var band = Domain.ApprovalRules.Evaluate(maxDays);
            Console.WriteLine($"  facade submit days={maxDays} band={band}");
        }
    }

    public sealed class ReconciliationGateProxy
    {
        private readonly bool _allow;
        public ReconciliationGateProxy(bool allowFulfill) => _allow = allowFulfill;
        public bool TryFulfill() => _allow;
    }
}

// =============================================================================
// PART 5 — Behavioral patterns
// =============================================================================
public static class Part5_Behavioral
{
    public static void Run()
    {
        Console.WriteLine("-- PART 5: Behavioral --");

        // Strategy: notification channel
        INotificationStrategy email = new EmailNotificationStrategy();
        INotificationStrategy audit = new AuditTrailStrategy();
        foreach (INotificationStrategy s in new[] { email, audit })
            s.Send("RM approval pending");

        // State: approval case stage transitions
        var machine = new ApprovalWorkflowMachine(Domain.ApprovalBand.RenewalManagerThenDealDesk);
        machine.OnAutoPolicyEvaluated();
        machine.Approve(Domain.Roles.RenewalManager);
        machine.Approve(Domain.Roles.DealDeskManager);
        Console.WriteLine($"  final state={machine.State}");

        // Chain of Responsibility: MediatR ValidationBehavior → Handler
        var pipeline = new ValidationBehavior(new CreateHandler());
        pipeline.Handle(new CreateRequest(MaxDays: 10));
        try { pipeline.Handle(new CreateRequest(MaxDays: 0)); }
        catch (Exception ex) { Console.WriteLine($"  validation caught: {ex.Message}"); }

        // Observer: domain events → outbox writer (sketch)
        var bus = new DomainEventBus();
        bus.Subscribe(e => Console.WriteLine($"  outbox enqueue: {e}"));
        bus.Publish("LicenseExtensionRequestCreated");

        // Command: MediatR IRequest
        Console.WriteLine("  (Command pattern ≡ MediatR IRequest/IRequestHandler)");
        Console.WriteLine();
    }

    public interface INotificationStrategy
    {
        void Send(string message);
    }

    public sealed class EmailNotificationStrategy : INotificationStrategy
    {
        public void Send(string message) => Console.WriteLine($"  EMAIL: {message}");
    }

    public sealed class AuditTrailStrategy : INotificationStrategy
    {
        public void Send(string message) => Console.WriteLine($"  AUDIT: {message}");
    }

    public sealed class ApprovalWorkflowMachine
    {
        private readonly Domain.ApprovalBand _band;
        public Domain.SagaState State { get; private set; } = Domain.SagaState.Requested;
        private bool _rm;
        private bool _dd;
        public ApprovalWorkflowMachine(Domain.ApprovalBand band) => _band = band;

        public void OnAutoPolicyEvaluated()
        {
            State = _band switch
            {
                Domain.ApprovalBand.AutoApprove => Domain.SagaState.Approved,
                Domain.ApprovalBand.Unsupported => Domain.SagaState.Rejected,
                _ => Domain.SagaState.PendingRenewalManagerApproval
            };
        }

        public void Approve(string role)
        {
            if (State == Domain.SagaState.Rejected || State == Domain.SagaState.Approved) return;
            if (role == Domain.Roles.RenewalManager &&
                State == Domain.SagaState.PendingRenewalManagerApproval)
            {
                _rm = true;
                State = _band == Domain.ApprovalBand.RenewalManagerThenDealDesk
                    ? Domain.SagaState.PendingDealDeskApproval
                    : Domain.SagaState.Approved;
                return;
            }
            if (role == Domain.Roles.DealDeskManager &&
                State == Domain.SagaState.PendingDealDeskApproval && _rm)
            {
                _dd = true;
                State = Domain.SagaState.Approved;
            }
        }
    }

    public sealed record CreateRequest(int MaxDays);

    public interface IHandler { void Handle(CreateRequest request); }

    public sealed class CreateHandler : IHandler
    {
        public void Handle(CreateRequest request) =>
            Console.WriteLine($"  handler create band={Domain.ApprovalRules.Evaluate(request.MaxDays)}");
    }

    public sealed class ValidationBehavior : IHandler
    {
        private readonly IHandler _next;
        public ValidationBehavior(IHandler next) => _next = next;
        public void Handle(CreateRequest request)
        {
            if (request.MaxDays < 1) throw new InvalidOperationException("MaxDays must be ≥ 1");
            _next.Handle(request);
        }
    }

    public sealed class DomainEventBus
    {
        private readonly List<Action<string>> _handlers = new();
        public void Subscribe(Action<string> h) => _handlers.Add(h);
        public void Publish(string e)
        {
            foreach (var h in _handlers) h(e);
        }
    }
}

// =============================================================================
// PART 6 — Concurrency LLD
// =============================================================================
public static class Part6_Concurrency
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 6: Concurrency LLD --");

        // Optimistic concurrency: row_version on LicenseExtensionRequest
        var row = new ConcurrentRow(RowVersion: 3);
        var ok = row.TryUpdate(expectedVersion: 3, mutate: r => r.Status = "InFulfillment");
        var conflict = row.TryUpdate(expectedVersion: 3, mutate: r => r.Status = "Completed");
        Console.WriteLine($"  optimistic update ok={ok} conflictDetected={!conflict} version={row.RowVersion}");

        // Lease claim with SKIP LOCKED semantics (in-memory simulation)
        var store = new InMemoryLeaseStore();
        store.Seed("outbox-1", "outbox-2", "outbox-3");
        var w1 = store.Claim("worker-A", take: 2);
        var w2 = store.Claim("worker-B", take: 2);
        Console.WriteLine($"  worker-A claimed={string.Join(",", w1)}");
        Console.WriteLine($"  worker-B claimed={string.Join(",", w2)}");

        // Selective retry claim: LogicalOperationId exclusive claim
        var retryClaims = new ConcurrentDictionary<Guid, string>();
        var logical = Guid.NewGuid();
        bool first = retryClaims.TryAdd(logical, "retry-gen-1");
        bool second = retryClaims.TryAdd(logical, "retry-gen-2");
        Console.WriteLine($"  retry claim first={first} second={second}");

        await Task.Delay(1); // keep async signature honest for demo host
        Console.WriteLine();
    }

    public sealed class ConcurrentRow
    {
        public long RowVersion { get; private set; }
        public string Status { get; set; } = "Requested";
        public ConcurrentRow(long RowVersion) => this.RowVersion = RowVersion;
        public bool TryUpdate(long expectedVersion, Action<ConcurrentRow> mutate)
        {
            if (RowVersion != expectedVersion) return false;
            mutate(this);
            RowVersion++;
            return true;
        }
    }

    public sealed class InMemoryLeaseStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string?> _owner = new();
        public void Seed(params string[] ids)
        {
            foreach (var id in ids) _owner[id] = null;
        }
        public List<string> Claim(string workerId, int take)
        {
            lock (_gate)
            {
                var batch = _owner.Where(kv => kv.Value is null).Take(take).Select(kv => kv.Key).ToList();
                foreach (var id in batch) _owner[id] = workerId;
                return batch;
            }
        }
    }
}

// =============================================================================
// PART 7 — Approval workflow state machine (classic → EntitleX)
// =============================================================================
/// <summary>
/// Deep design replacing generic "order state machine" toys.
/// Mirrors ApprovalCase stages + LicenseExtensionSaga transitions.
/// </summary>
public static class Part7_ApprovalStateMachine
{
    public static void Run()
    {
        Console.WriteLine("-- PART 7: Approval workflow state machine --");
        var cases = new[]
        {
            ("req-auto", 10),
            ("req-rm", 45),
            ("req-dd", 120),
            ("req-bad", 400),
        };
        foreach (var (id, days) in cases)
        {
            var wf = new Part5_Behavioral.ApprovalWorkflowMachine(Domain.ApprovalRules.Evaluate(days));
            wf.OnAutoPolicyEvaluated();
            if (wf.State == Domain.SagaState.PendingRenewalManagerApproval)
            {
                wf.Approve(Domain.Roles.RenewalManager);
                if (wf.State == Domain.SagaState.PendingDealDeskApproval)
                    wf.Approve(Domain.Roles.DealDeskManager);
            }
            Console.WriteLine($"  {id} days={days} → {wf.State}");
        }

        // Illegal transition guard
        var illegal = new Part5_Behavioral.ApprovalWorkflowMachine(Domain.ApprovalBand.RenewalManagerThenDealDesk);
        illegal.OnAutoPolicyEvaluated();
        illegal.Approve(Domain.Roles.DealDeskManager); // RM not done — no-op
        Console.WriteLine($"  deal-desk-before-rm still={illegal.State}");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 8 — Idempotency store (LRU + TTL)
// =============================================================================
/// <summary>
/// Models ApiIdempotencyRecords + ProcessedMessages.
/// Paths: src/EntitleX.Infrastructure/Retry/ApiIdempotencyRecord.cs
///        src/EntitleX.Infrastructure/Outbox/ProcessedMessage.cs
/// </summary>
public static class Part8_IdempotencyStore
{
    public static void Run()
    {
        Console.WriteLine("-- PART 8: Idempotency store (LRU/TTL) --");
        var store = new LruTtlIdempotencyStore(capacity: 3, ttl: TimeSpan.FromMilliseconds(50));
        Console.WriteLine($"  first claim keyA: {store.TryClaim("keyA")}");
        Console.WriteLine($"  replay keyA: {store.TryClaim("keyA")}");
        store.TryClaim("keyB");
        store.TryClaim("keyC");
        store.TryClaim("keyD"); // evicts oldest if over capacity
        Thread.Sleep(60);
        Console.WriteLine($"  after TTL keyA reclaimable? {store.TryClaim("keyA")}");
        Console.WriteLine();
    }

    public sealed class LruTtlIdempotencyStore
    {
        private readonly int _capacity;
        private readonly TimeSpan _ttl;
        private readonly Dictionary<string, LinkedListNode<(string key, DateTimeOffset exp)>> _map = new();
        private readonly LinkedList<(string key, DateTimeOffset exp)> _lru = new();
        public LruTtlIdempotencyStore(int capacity, TimeSpan ttl)
        {
            _capacity = capacity; _ttl = ttl;
        }

        public bool TryClaim(string key)
        {
            var now = DateTimeOffset.UtcNow;
            PurgeExpired(now);
            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.exp > now) return false; // duplicate
                _lru.Remove(node);
                _map.Remove(key);
            }
            while (_map.Count >= _capacity && _lru.Last is not null)
            {
                var last = _lru.Last.Value;
                _lru.RemoveLast();
                _map.Remove(last.key);
            }
            var exp = now.Add(_ttl);
            var n = _lru.AddFirst((key, exp));
            _map[key] = n;
            return true;
        }

        private void PurgeExpired(DateTimeOffset now)
        {
            while (_lru.Last is not null && _lru.Last.Value.exp <= now)
            {
                var last = _lru.Last.Value;
                _lru.RemoveLast();
                _map.Remove(last.key);
            }
        }
    }
}

// =============================================================================
// PART 9 — Rate limiter / bulkhead (Polly analogy)
// =============================================================================
/// <summary>
/// Mirrors ProviderOptions.BulkheadMaxParallelization / QueueLimit and Polly
/// registration in ProviderHttpClientRegistration.
/// </summary>
public static class Part9_RateLimiter
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 9: Rate limiter / bulkhead --");
        var bulkhead = new SimpleBulkhead(maxParallel: 2, queueLimit: 2);
        var tasks = Enumerable.Range(0, 5)
            .Select(i => Task.Run(async () =>
            {
                if (!await bulkhead.TryEnterAsync())
                {
                    Console.WriteLine($"  req-{i} rejected (bulkhead full)");
                    return;
                }
                try
                {
                    Console.WriteLine($"  req-{i} entered");
                    await Task.Delay(20);
                }
                finally { bulkhead.Exit(); }
            }))
            .ToArray();
        await Task.WhenAll(tasks);
        Console.WriteLine();
    }

    public sealed class SimpleBulkhead
    {
        private readonly SemaphoreSlim _sem;
        private readonly int _queueLimit;
        private int _queued;
        public SimpleBulkhead(int maxParallel, int queueLimit)
        {
            _sem = new SemaphoreSlim(maxParallel, maxParallel);
            _queueLimit = queueLimit;
        }
        public async Task<bool> TryEnterAsync()
        {
            if (Interlocked.Increment(ref _queued) > _queueLimit + _sem.CurrentCount)
            {
                Interlocked.Decrement(ref _queued);
                return false;
            }
            await _sem.WaitAsync();
            Interlocked.Decrement(ref _queued);
            return true;
        }
        public void Exit() => _sem.Release();
    }
}

// =============================================================================
// PART 10 — Outbox claim lease
// =============================================================================
/// <summary>
/// Teaching model for OutboxClaimer FOR UPDATE SKIP LOCKED + locked_until_utc.
/// Path: src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
/// </summary>
public static class Part10_OutboxLease
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 10: Outbox claim lease --");
        var outbox = new ToyOutbox();
        outbox.Enqueue("FulfillmentCommandV1", "payload-1");
        outbox.Enqueue("FulfillmentCommandV1", "payload-2");
        outbox.Enqueue("ProviderResultV1", "payload-3");

        var batch = await outbox.ClaimBatchAsync("outbox-publisher-1", batchSize: 2, lockSeconds: 30);
        Console.WriteLine($"  claimed={batch.Count}");
        foreach (var m in batch)
            Console.WriteLine($"    {m.Id} status={m.Status} lockedBy={m.LockedBy}");

        // Second worker sees remaining only
        var batch2 = await outbox.ClaimBatchAsync("outbox-publisher-2", batchSize: 2, lockSeconds: 30);
        Console.WriteLine($"  worker2 claimed={batch2.Count}");
        Console.WriteLine();
    }

    public sealed class ToyOutboxMessage
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string MessageType { get; init; } = "";
        public string Payload { get; init; } = "";
        public string Status { get; set; } = "Pending";
        public string? LockedBy { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
        public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class ToyOutbox
    {
        private readonly object _gate = new();
        private readonly List<ToyOutboxMessage> _messages = new();
        public void Enqueue(string type, string payload) =>
            _messages.Add(new ToyOutboxMessage { MessageType = type, Payload = payload });

        public Task<IReadOnlyList<ToyOutboxMessage>> ClaimBatchAsync(string workerId, int batchSize, int lockSeconds)
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                var claimed = _messages
                    .Where(m =>
                        (m.Status == "Pending" && m.AvailableAtUtc <= now) ||
                        (m.Status == "Processing" && m.LockedUntilUtc < now))
                    .OrderBy(m => m.AvailableAtUtc)
                    .Take(batchSize)
                    .ToList();
                foreach (var m in claimed)
                {
                    m.Status = "Processing";
                    m.LockedBy = workerId;
                    m.LockedUntilUtc = now.AddSeconds(lockSeconds);
                }
                return Task.FromResult((IReadOnlyList<ToyOutboxMessage>)claimed);
            }
        }
    }
}

// =============================================================================
// PART 11 — Saga orchestrator
// =============================================================================
/// <summary>
/// Forward-recovery saga: never undo CloudShield success when NetworkGuard fails.
/// Selective retry creates a new AttemptId with ParentId lineage.
/// </summary>
public static class Part11_SagaOrchestrator
{
    public static void Run()
    {
        Console.WriteLine("-- PART 11: Saga orchestrator --");
        var orch = new SagaOrchestrator();
        var items = new List<Domain.ExtensionItem>
        {
            new() { EntitlementId = "CS-1", Provider = Domain.ProviderSystem.CloudShield, ExtensionDays = 60 },
            new() { EntitlementId = "NG-1", Provider = Domain.ProviderSystem.NetworkGuard, ExtensionDays = 60 },
        };
        orch.Start(items);
        orch.RecordProviderResult(items[0].LogicalOperationId, success: true);
        orch.RecordProviderResult(items[1].LogicalOperationId, success: false);
        Console.WriteLine($"  saga state={orch.State} (forward recovery → PartiallyCompleted/Failed path)");
        Console.WriteLine($"  succeeded={orch.Succeeded} failed={orch.Failed}");

        // Reconciliation gate
        if (orch.Failed > 0 && orch.Succeeded > 0)
        {
            orch.MarkReconciliationRequired();
            Console.WriteLine($"  gate → {orch.State}");
        }
        Console.WriteLine();
    }

    public sealed class SagaOrchestrator
    {
        private readonly Dictionary<Domain.LogicalOperationId, Domain.ItemOutcomeStatus> _outcomes = new();
        public Domain.SagaState State { get; private set; } = Domain.SagaState.Requested;
        public int Succeeded => _outcomes.Values.Count(v => v == Domain.ItemOutcomeStatus.Succeeded);
        public int Failed => _outcomes.Values.Count(v => v == Domain.ItemOutcomeStatus.Failed);

        public void Start(IEnumerable<Domain.ExtensionItem> items)
        {
            foreach (var i in items)
                _outcomes[i.LogicalOperationId] = Domain.ItemOutcomeStatus.Pending;
            State = Domain.SagaState.Processing;
        }

        public void RecordProviderResult(Domain.LogicalOperationId id, bool success)
        {
            _outcomes[id] = success ? Domain.ItemOutcomeStatus.Succeeded : Domain.ItemOutcomeStatus.Failed;
            if (_outcomes.Values.All(v => v != Domain.ItemOutcomeStatus.Pending))
            {
                State = (Succeeded, Failed) switch
                {
                    (_, 0) => Domain.SagaState.Completed,
                    (0, _) => Domain.SagaState.Failed,
                    _ => Domain.SagaState.PartiallyCompleted
                };
            }
        }

        public void MarkReconciliationRequired() =>
            State = Domain.SagaState.ReconciliationRequired;
    }
}

// =============================================================================
// PART 12 — Notification fan-out
// =============================================================================
public static class Part12_NotificationFanOut
{
    public static void Run()
    {
        Console.WriteLine("-- PART 12: Notification fan-out --");
        var fanout = new NotificationFanOut(new Part5_Behavioral.INotificationStrategy[]
        {
            new Part5_Behavioral.EmailNotificationStrategy(),
            new Part5_Behavioral.AuditTrailStrategy(),
            new SlackStyleStrategy(),
        });
        fanout.Publish("Approval pending for RenewalManager — case 91–365 band");
        Console.WriteLine();
    }

    public sealed class SlackStyleStrategy : Part5_Behavioral.INotificationStrategy
    {
        public void Send(string message) => Console.WriteLine($"  SLACK: {message}");
    }

    public sealed class NotificationFanOut
    {
        private readonly IReadOnlyList<Part5_Behavioral.INotificationStrategy> _strategies;
        public NotificationFanOut(IReadOnlyList<Part5_Behavioral.INotificationStrategy> strategies) =>
            _strategies = strategies;
        public void Publish(string message)
        {
            foreach (var s in _strategies) s.Send(message);
        }
    }
}

// =============================================================================
// PART 13 — Provider adapter
// =============================================================================
public static class Part13_ProviderAdapter
{
    public static void Run()
    {
        Console.WriteLine("-- PART 13: Provider adapter + strategy --");
        var router = new ProviderRouter(new Part4_Structural.IProviderAdapter[]
        {
            new Part4_Structural.CloudShieldAdapter(),
            new Part4_Structural.NetworkGuardAdapter(),
        });
        router.Dispatch(Domain.ProviderSystem.CloudShield, "CS-ENT-044");
        router.Dispatch(Domain.ProviderSystem.NetworkGuard, "NG-ENT-009");
        Console.WriteLine();
    }

    public sealed class ProviderRouter
    {
        private readonly Dictionary<string, Part4_Structural.IProviderAdapter> _byName;
        public ProviderRouter(IEnumerable<Part4_Structural.IProviderAdapter> adapters) =>
            _byName = adapters.ToDictionary(a => a.Name, StringComparer.Ordinal);
        public void Dispatch(Domain.ProviderSystem provider, string entitlementId)
        {
            var name = provider.ToString();
            _byName[name].Extend(entitlementId);
        }
    }
}

// =============================================================================
// PART 14 — Selective retry lineage
// =============================================================================
/// <summary>
/// LogicalOperationId stable across retries; AttemptId new each generation;
/// ParentId points at prior AttemptId. See FailedItemSelector + RetryItemClaim.
/// </summary>
public static class Part14_SelectiveRetryLineage
{
    public static void Run()
    {
        Console.WriteLine("-- PART 14: Selective retry lineage --");
        var original = new Domain.ExtensionItem
        {
            EntitlementId = "NG-1",
            Provider = Domain.ProviderSystem.NetworkGuard,
            ExtensionDays = 60,
            Status = Domain.ItemOutcomeStatus.Failed
        };
        Console.WriteLine($"  gen0 Logical={original.LogicalOperationId} Attempt={original.AttemptId.Value}");

        var retry = new Domain.ExtensionItem
        {
            LogicalOperationId = original.LogicalOperationId, // STABLE
            AttemptId = Domain.AttemptId.New(),               // NEW
            ParentId = original.AttemptId,                    // LINEAGE
            EntitlementId = original.EntitlementId,
            Provider = original.Provider,
            ExtensionDays = original.ExtensionDays,
        };
        Console.WriteLine($"  gen1 Logical={retry.LogicalOperationId} Attempt={retry.AttemptId.Value}");
        Console.WriteLine($"  ParentId={retry.ParentId!.Value} (points at gen0 AttemptId)");
        Console.WriteLine($"  same LogicalOperationId? {original.LogicalOperationId == retry.LogicalOperationId}");

        // Succeeded items are NEVER selected (forward recovery)
        var succeeded = new Domain.ExtensionItem { Status = Domain.ItemOutcomeStatus.Succeeded };
        var failedOnly = new[] { original, succeeded }
            .Where(i => i.Status == Domain.ItemOutcomeStatus.Failed)
            .ToList();
        Console.WriteLine($"  eligible failed count={failedOnly.Count}");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 15 — Brief toy mappings (prefer EntitleX designs above)
// =============================================================================
public static class Part15_ToyMappings
{
    public static void Run()
    {
        Console.WriteLine("-- PART 15: Toy → EntitleX mappings (brief) --");
        Console.WriteLine("""
  Elevator scheduling     → OutboxClaimer lease + worker fair polling
  Parking lot             → Bulkhead slots for CloudShield/NetworkGuard HTTP
  Splitwise               → EntitlementAggregation across providers (read model)
  TicTacToe board         → Saga state grid is the wrong metaphor — use explicit FSM
  Hotel booking lock      → ApiIdempotencyRecords + RetryItemClaims
  Snake & Ladder          → Approval stage progression RM → DealDesk
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 16 — Interview checklist
// =============================================================================
public static class Part16_Interview
{
    public static void Print()
    {
        Console.WriteLine("-- PART 16: Interview checklist --");
        Console.WriteLine("""
  □ Draw the approval band thresholds (1–30 / 31–90 / 91–365 / >365).
  □ Explain forward recovery vs compensation (why no undo on CloudShield success).
  □ Sketch LogicalOperationId / AttemptId / ParentId lineage for selective retry.
  □ Describe outbox claim: Pending|stale Processing + FOR UPDATE SKIP LOCKED.
  □ Map SOLID to IProviderAdapter / ValidationBehavior / IUnitOfWork.
  □ Call out reconciliation gate before re-fulfillment.
  □ Roles: RenewalRepresentative creates; RM/DealDesk decide; PlatformAdmin ops.
  □ Idempotency at API (ApiIdempotencyRecords) AND consumer (ProcessedMessages).
""");
    }
}
}
