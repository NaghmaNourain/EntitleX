// =============================================================================
 // EntitleX .NET COLLECTIONS GUIDE
 // Educational / interview-style walkthrough framed around the EntitleX
 // license-extension control plane (modular monolith, Clean Architecture).
 //
 // Target: .NET 8  |  No external NuGet packages required.
 // Run:    `dotnet script` is NOT required — paste into a console app, or:
 //          dotnet new console -n Guides.Collections && copy this file over Program.cs
 //
 // Real domain paths referenced in comments:
 //   src/EntitleX.Domain/LicenseExtension/LicenseExtensionRequest.cs
 //   src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
 //   src/EntitleX.Domain/LicenseExtension/FailedItemSelector.cs
 //   src/EntitleX.BuildingBlocks/Ids/*.cs
 //   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
 //   src/workers/EntitleX.OutboxPublisher/
 //   src/workers/EntitleX.FulfillmentWorker/
 //
 // TABLE OF CONTENTS
 //   PART 1  — Collection interface hierarchy
 //   PART 2  — Arrays & Span<T> (message keys / bulk payloads)
 //   PART 3  — List<T> + AsReadOnly (aggregate item encapsulation)
 //   PART 4  — LinkedList<T> (when NOT to use it in EntitleX)
 //   PART 5  — Stack / Queue / PriorityQueue (approval aging)
 //   PART 6  — Dictionary (saga / request status machines)
 //   PART 7  — HashSet / SortedSet (failed LogicalOperationId membership)
 //   PART 8  — SortedDictionary vs SortedList
 //   PART 9  — Equality & ordering (readonly record struct IDs)
 //   PART 10 — LINQ / yield (selectors vs hot loops)
 //   PART 11 — ReadOnly / Immutable / Frozen collections
 //   PART 12 — Concurrent collections & Channel (outbox drain analogy)
 //   PART 13 — Specialized collections
 //   PART 14 — Performance cheat notes
 //   PART 15 — Interview problems (LRU processed-message cache, Top-K approvals)
 //   PART 16 — Interview cheat sheet
 // =============================================================================

#nullable enable
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EntitleX.Guides.Collections;

// -----------------------------------------------------------------------------
 // Simulated domain vocabulary (mirrors BuildingBlocks / Domain — keep inline so
 // this file runs without referencing the solution projects).
 // -----------------------------------------------------------------------------

/// <summary>Stable request identity. Real: src/EntitleX.BuildingBlocks/Ids/RequestId.cs</summary>
public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

/// <summary>Per-item identity inside a LicenseExtensionRequest.</summary>
public readonly record struct RequestItemId(Guid Value)
{
    public static RequestItemId New() => new(Guid.NewGuid());
}

/// <summary>Provider-facing entitlement key; also Kafka partition key.</summary>
public readonly record struct EntitlementId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
 // Preserved across selective retries (forward recovery). Provider unique index
 // on logical_operation_id makes re-apply a no-op.
 // Real: src/EntitleX.BuildingBlocks/Ids/LogicalOperationId.cs
 // </summary>
public readonly record struct LogicalOperationId(Guid Value)
{
    public static LogicalOperationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

/// <summary>Minted fresh on every selective-retry attempt; never reused.</summary>
public readonly record struct AttemptId(Guid Value)
{
    public static AttemptId New() => new(Guid.NewGuid());
}

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
    Requested, PendingRenewalManagerApproval, PendingDealDeskApproval, Approved,
    QueuedForFulfillment, Processing, PartiallyCompleted, Completed, Failed,
    ReconciliationRequired, Escalated, Rejected
}

public enum ApprovalBand
{
    AutoApprove, RenewalManagerOnly, RenewalManagerThenDealDesk, Unsupported
}

public enum OutboxStatus { Pending, Locked, Published, Poison }

public sealed record OutboxMessage(
    MessageId Id,
    string MessageType,
    string Payload,
    OutboxStatus Status,
    DateTimeOffset AvailableAtUtc,
    EntitlementId? PartitionKey);

public sealed record ApprovalCaseSummary(
    Guid ApprovalCaseId,
    RequestId RequestId,
    ApprovalBand Band,
    DateTimeOffset CreatedAtUtc,
    int DaysWaiting);

public sealed record SagaItemOutcome(
    LogicalOperationId LogicalOperationId,
    EntitlementId EntitlementId,
    ProviderSystem Provider,
    string Status); // Success | Failed | Pending | Uncertain

// =============================================================================
public static class Program
{
    public static void Main()
    {
        Console.WriteLine("=== EntitleX Collections Guide (.NET 8) ===\n");

        Part1_InterfaceHierarchy();
        Part2_ArraysAndSpan();
        Part3_ListAndAsReadOnly();
        Part4_LinkedList();
        Part5_StackQueuePriorityQueue();
        Part6_DictionaryStatusMachines();
        Part7_HashSetAndSortedSet();
        Part8_SortedDictionaryVsSortedList();
        Part9_EqualityAndOrdering();
        Part10_LinqAndYield();
        Part11_ReadOnlyImmutableFrozen();
        Part12_ConcurrentAndChannel();
        Part13_Specialized();
        Part14_PerformanceNotes();
        Part15_InterviewProblems();
        Part16_CheatSheet();

        Console.WriteLine("\n=== Done. Re-read PART 16 before interviews. ===");
    }

    // =========================================================================
    // PART 1 — Collection interface hierarchy
    // =========================================================================
    static void Part1_InterfaceHierarchy()
    {
        Section("PART 1 — Collection interface hierarchy");

        // IEnumerable<T>  →  ICollection<T>  →  IList<T>
        //                 ↘  ISet<T>
        //                 ↘  IDictionary<TKey,TValue> (via IEnumerable<KeyValuePair>)
        //
        // EntitleX APIs almost always expose IReadOnlyList / IReadOnlyDictionary
        // so callers cannot mutate aggregate internals (LicenseExtensionRequest.Items).

        IEnumerable<EntitlementId> entitlements = new[]
        {
            new EntitlementId("ent-cloudshield-001"),
            new EntitlementId("ent-networkguard-014")
        };

        // ICollection adds Count + mutation surface — prefer IReadOnlyCollection
        // on public aggregate properties.
        IReadOnlyCollection<EntitlementId> readOnly = entitlements.ToArray();
        Console.WriteLine($"IReadOnlyCollection.Count = {readOnly.Count}");

        // IList adds indexer — useful for paging keyset cursors over in-memory demos,
        // but production audit uses keyset on (occurred_at, id), not OFFSET.
        IList<string> providers = new List<string> { "CloudShield", "NetworkGuard" };
        Console.WriteLine($"IList[0] = {providers[0]} (ProviderSystem.CloudShield)");

        // Interview tip: prefer the *narrowest* interface that still expresses intent.
        // Returning List<T> from a domain method invites callers to Add() and break invariants.
    }

    // =========================================================================
    // PART 2 — Arrays & Span<T>
    // =========================================================================
    static void Part2_ArraysAndSpan()
    {
        Section("PART 2 — Arrays & Span<T>");

        // Arrays: fixed size. Great for known-bounded provider fan-out (exactly 2 systems).
        ProviderSystem[] fanOut = [ProviderSystem.CloudShield, ProviderSystem.NetworkGuard];
        Console.WriteLine($"Provider fan-out array length = {fanOut.Length} (always 2 in EntitleX)");

        // Span<T> / ReadOnlySpan<T>: stack-friendly views, no allocation when slicing.
        // Use when parsing outbox / SQS message keys like "fulfillment:{requestId}:{messageId}".
        string messageKey = $"fulfillment:{RequestId.New()}:{MessageId.New()}";
        ReadOnlySpan<char> keySpan = messageKey.AsSpan();
        int firstColon = keySpan.IndexOf(':');
        ReadOnlySpan<char> kind = keySpan[..firstColon];
        Console.WriteLine($"Span-parsed message kind = '{kind}' (no Substring alloc)");

        // MemoryMarshal / stackalloc for tiny ID buffers when building partition keys.
        Span<char> scratch = stackalloc char[8];
        "ent-demo".AsSpan().CopyTo(scratch);
        Console.WriteLine($"stackalloc scratch starts with '{scratch[0]}'");

        // Interview: Array is covariant (string[] → object[]) — can throw ArrayTypeMismatchException.
        // List<T> is invariant. Prefer List / IReadOnlyList for domain collections of IDs.
    }

    // =========================================================================
    // PART 3 — List<T> + AsReadOnly (aggregate encapsulation)
    // =========================================================================
    static void Part3_ListAndAsReadOnly()
    {
        Section("PART 3 — List<T> + AsReadOnly");

        // Mirrors LicenseExtensionRequest:
        //   private readonly List<LicenseExtensionRequestItem> _items = [];
        //   public IReadOnlyList<...> Items => _items.AsReadOnly();
        //   MaxItems = 1000
        var aggregate = new LicenseExtensionRequestDemo(RequestId.New(), maxItems: 1000);

        for (int i = 0; i < 3; i++)
        {
            aggregate.AddItem(
                new EntitlementId($"ent-{i:D3}"),
                ProviderSystem.CloudShield,
                LogicalOperationId.New(),
                AttemptId.New());
        }

        Console.WriteLine($"Items exposed as IReadOnlyList.Count = {aggregate.Items.Count}");
        Console.WriteLine($"AsReadOnly wrapper type = {aggregate.Items.GetType().Name}");

        // Callers cannot Add through Items — compile-time if typed as IReadOnlyList,
        // and runtime NotSupportedException if they cast to IList and try.
        try
        {
            ((IList<RequestItemDemo>)aggregate.Items).Add(
                new RequestItemDemo(RequestItemId.New(), new EntitlementId("x"),
                    ProviderSystem.NetworkGuard, LogicalOperationId.New(), AttemptId.New()));
        }
        catch (NotSupportedException)
        {
            Console.WriteLine("AsReadOnly correctly blocked external Add (invariant preserved).");
        }

        // Capacity tip: when attaching up to 1000 items, set Capacity to avoid doubling copies.
        var bulk = new List<RequestItemDemo>(capacity: 1000);
        Console.WriteLine($"Pre-sized Capacity = {bulk.Capacity} (matches MaxItems=1000)");
    }

    // =========================================================================
    // PART 4 — LinkedList<T>
    // =========================================================================
    static void Part4_LinkedList()
    {
        Section("PART 4 — LinkedList<T> (usually the wrong tool in EntitleX)");

        // LinkedList shines for O(1) insert/remove *given a node reference*.
        // EntitleX aggregates are EF-owned collections; we never walk node pointers
        // for saga transitions. Prefer List for items, Dictionary for transitions,
        // ConcurrentQueue/Channel for worker drains.
        var ll = new LinkedList<string>();
        ll.AddLast("Requested");
        ll.AddLast("PendingRenewalManagerApproval");
        ll.AddLast("Approved");
        Console.WriteLine($"LinkedList demo length = {ll.Count} — pedagogical only");
        Console.WriteLine("Prefer Dictionary<SagaState, SagaState[]> for AllowedTransitions.");
    }

    // =========================================================================
    // PART 5 — Stack / Queue / PriorityQueue
    // =========================================================================
    static void Part5_StackQueuePriorityQueue()
    {
        Section("PART 5 — Stack / Queue / PriorityQueue");

        // Stack: LIFO — useful for undo of *in-memory* draft edits (UI), not for
        // compensating provider extensions (EntitleX uses forward recovery).
        var undo = new Stack<string>();
        undo.Push("Draft.notes changed");
        undo.Push("Draft.item added");
        Console.WriteLine($"Stack.Pop undo = {undo.Pop()}");

        // Queue: FIFO — analogy for outbox AvailableAtUtc ordering *within a single
        // publisher process*. Real claim uses FOR UPDATE SKIP LOCKED (OutboxClaimer).
        var localDrain = new Queue<OutboxMessage>();
        localDrain.Enqueue(new OutboxMessage(
            MessageId.New(), "FulfillmentCommand", "{}", OutboxStatus.Pending,
            DateTimeOffset.UtcNow.AddSeconds(-1), new EntitlementId("ent-1")));
        Console.WriteLine($"Queue.Peek type = {localDrain.Peek().MessageType}");

        // PriorityQueue: approval aging — oldest pending ApprovalCase surfaces first.
        // Real pending lists use keyset pagination; this models an in-memory ops board.
        var aging = new PriorityQueue<ApprovalCaseSummary, long>();
        var now = DateTimeOffset.UtcNow;
        void EnqueueApproval(ApprovalBand band, int daysWaiting, string entitlementHint)
        {
            var summary = new ApprovalCaseSummary(
                Guid.NewGuid(), RequestId.New(), band,
                now.AddDays(-daysWaiting), daysWaiting);
            // Priority = CreatedAtUtc ticks ascending → oldest first
            aging.Enqueue(summary, summary.CreatedAtUtc.UtcTicks);
            _ = entitlementHint;
        }

        EnqueueApproval(ApprovalBand.RenewalManagerOnly, daysWaiting: 2, "ent-a");
        EnqueueApproval(ApprovalBand.RenewalManagerThenDealDesk, daysWaiting: 9, "ent-b");
        EnqueueApproval(ApprovalBand.RenewalManagerOnly, daysWaiting: 5, "ent-c");

        var hottest = aging.Dequeue();
        Console.WriteLine(
            $"PriorityQueue oldest approval waited {hottest.DaysWaiting} days " +
            $"(band={hottest.Band}) — escalate before auto-approve timeouts fire.");
        // Note: EntitleX never auto-approves on timeout; it escalates.
    }

    // =========================================================================
    // PART 6 — Dictionary (status machines)
    // =========================================================================
    static void Part6_DictionaryStatusMachines()
    {
        Section("PART 6 — Dictionary status machines");

        // Exact pattern from LicenseExtensionSaga.AllowedTransitions and
        // LicenseExtensionRequest.AllowedTransitions — static readonly maps.
        IReadOnlyDictionary<SagaState, SagaState[]> allowed =
            new Dictionary<SagaState, SagaState[]>
            {
                [SagaState.Requested] =
                [
                    SagaState.PendingRenewalManagerApproval,
                    SagaState.Approved,
                    SagaState.Rejected,
                    SagaState.Escalated
                ],
                [SagaState.PendingRenewalManagerApproval] =
                [
                    SagaState.PendingDealDeskApproval,
                    SagaState.Approved,
                    SagaState.Rejected,
                    SagaState.Escalated
                ],
                [SagaState.Approved] = [SagaState.QueuedForFulfillment],
                [SagaState.QueuedForFulfillment] =
                [
                    SagaState.Processing,
                    SagaState.ReconciliationRequired,
                    SagaState.Escalated
                ],
                [SagaState.Processing] =
                [
                    SagaState.PartiallyCompleted,
                    SagaState.Completed,
                    SagaState.Failed,
                    SagaState.ReconciliationRequired
                ],
                [SagaState.Completed] = [],
                [SagaState.Failed] = [],
                [SagaState.Rejected] = []
            };

        bool CanTransition(SagaState from, SagaState to) =>
            allowed.TryGetValue(from, out var next) && next.Contains(to);

        Console.WriteLine(
            $"Requested → Approved allowed? {CanTransition(SagaState.Requested, SagaState.Approved)}");
        Console.WriteLine(
            $"Completed → Processing allowed? {CanTransition(SagaState.Completed, SagaState.Processing)}");

        // GetValueOrDefault / TryGetValue — prefer TryGetValue to avoid KeyNotFoundException
        // when projecting unknown outbox MessageType handlers.
        var handlers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FulfillmentCommand"] = "FulfillmentWorker",
            ["ProviderResult"] = "ProviderResultsWorker"
        };
        Console.WriteLine(
            handlers.TryGetValue("FulfillmentCommand", out var worker)
                ? $"Route → {worker}"
                : "No handler");
    }

    // =========================================================================
    // PART 7 — HashSet / SortedSet
    // =========================================================================
    static void Part7_HashSetAndSortedSet()
    {
        Section("PART 7 — HashSet / SortedSet");

        // FailedItemSelector builds a HashSet of failed LogicalOperationIds, then
        // filters source.Items with Contains — O(1) average membership.
        // Real: src/EntitleX.Domain/LicenseExtension/FailedItemSelector.cs
        var failedLogicalIds = new HashSet<LogicalOperationId>
        {
            LogicalOperationId.New(),
            LogicalOperationId.New()
        };
        var probe = failedLogicalIds.First();
        Console.WriteLine($"HashSet.Contains(failed LogicalOperationId) = {failedLogicalIds.Contains(probe)}");

        // Selective retry MUST NOT include successes (forward recovery).
        var successIds = new HashSet<LogicalOperationId> { LogicalOperationId.New() };
        Console.WriteLine(
            $"Intersection failed∩success count = {failedLogicalIds.Intersect(successIds).Count()} (expect 0)");

        // SortedSet: ordered unique entitlement ids for deterministic Kafka key listing in demos.
        var sortedEntitlements = new SortedSet<string>(StringComparer.Ordinal)
        {
            "ent-networkguard-014",
            "ent-cloudshield-001",
            "ent-cloudshield-002"
        };
        Console.WriteLine($"SortedSet first = {sortedEntitlements.Min}");
    }

    // =========================================================================
    // PART 8 — SortedDictionary vs SortedList
    // =========================================================================
    static void Part8_SortedDictionaryVsSortedList()
    {
        Section("PART 8 — SortedDictionary vs SortedList");

        // SortedDictionary: tree-backed, O(log n) insert — better when mutating often.
        // SortedList: array-backed, faster indexed access, costly mid-insert shifts.
        // EntitleX persisted indexes are Postgres B-trees; these are in-memory teaching tools.

        var byBand = new SortedDictionary<ApprovalBand, int>
        {
            [ApprovalBand.AutoApprove] = 120,
            [ApprovalBand.RenewalManagerOnly] = 45,
            [ApprovalBand.RenewalManagerThenDealDesk] = 12,
            [ApprovalBand.Unsupported] = 1
        };
        Console.WriteLine("SortedDictionary bands (enum order):");
        foreach (var (band, count) in byBand)
            Console.WriteLine($"  {band} → {count} requests");

        var sortedList = new SortedList<string, int>
        {
            ["CloudShield"] = 2,
            ["NetworkGuard"] = 2
        };
        Console.WriteLine($"SortedList index of NetworkGuard = {sortedList.IndexOfKey("NetworkGuard")}");
    }

    // =========================================================================
    // PART 9 — Equality & ordering
    // =========================================================================
    static void Part9_EqualityAndOrdering()
    {
        Section("PART 9 — Equality & ordering (readonly record struct IDs)");

        // readonly record struct gives value equality + GetHashCode for free —
        // perfect Dictionary/HashSet keys for RequestId, LogicalOperationId, AttemptId.
        var a = new LogicalOperationId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var b = new LogicalOperationId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Console.WriteLine($"record struct equality a==b → {a == b}");
        Console.WriteLine($"GetHashCode equal? {a.GetHashCode() == b.GetHashCode()}");

        // EntitlementId wraps string — be explicit about comparer when used as dict keys
        // if casing might vary from provider payloads.
        var map = new Dictionary<EntitlementId, ProviderSystem>
        {
            [new EntitlementId("ent-1")] = ProviderSystem.CloudShield
        };
        Console.WriteLine($"Lookup ent-1 → {map[new EntitlementId("ent-1")]}");

        // Ordering: IComparable for PriorityQueue priorities; IDs themselves usually
        // compare via Guid/string ordinal — don't sort by RequestId for UX; sort by CreatedAtUtc.
    }

    // =========================================================================
    // PART 10 — LINQ / yield
    // =========================================================================
    static void Part10_LinqAndYield()
    {
        Section("PART 10 — LINQ / yield");

        var outcomes = new List<SagaItemOutcome>
        {
            new(LogicalOperationId.New(), new EntitlementId("e1"), ProviderSystem.CloudShield, "Success"),
            new(LogicalOperationId.New(), new EntitlementId("e2"), ProviderSystem.NetworkGuard, "Failed"),
            new(LogicalOperationId.New(), new EntitlementId("e3"), ProviderSystem.CloudShield, "Failed")
        };

        // Domain selectors (FailedItemSelector) use LINQ for clarity — OK at ≤1000 items.
        var failed = outcomes.Where(o => o.Status == "Failed").Select(o => o.LogicalOperationId).ToHashSet();
        Console.WriteLine($"LINQ failed count = {failed.Count}");

        // yield return: stream retry history without buffering (RetryHistoryEntry append-only).
        foreach (var entry in StreamRetryLineage(parentGenerations: 3))
            Console.WriteLine($"  yield lineage ParentId chain step → {entry}");

        // Hot path caution: inside OutboxPublisher tight loops, prefer foreach + manual
        // filtering over multiple deferred LINQ chains that re-enumerate.
    }

    static IEnumerable<string> StreamRetryLineage(int parentGenerations)
    {
        RequestId? parent = null;
        for (int i = 0; i < parentGenerations; i++)
        {
            var id = RequestId.New();
            yield return $"RequestId={id} ParentId={(parent?.ToString() ?? "null")} AttemptId={AttemptId.New()}";
            parent = id;
        }
    }

    // =========================================================================
    // PART 11 — ReadOnly / Immutable / Frozen
    // =========================================================================
    static void Part11_ReadOnlyImmutableFrozen()
    {
        Section("PART 11 — ReadOnly / Immutable / Frozen");

        // ReadOnlyCollection: wrapper from List.AsReadOnly() — cheap view, still shares storage.
        var mutable = new List<ProviderSystem> { ProviderSystem.CloudShield, ProviderSystem.NetworkGuard };
        ReadOnlyCollection<ProviderSystem> view = mutable.AsReadOnly();
        mutable.Add(ProviderSystem.CloudShield); // view sees the add!
        Console.WriteLine($"ReadOnlyCollection still reflects list mutates → Count={view.Count} (gotcha)");

        // ImmutableList: structural share; safe to publish across threads without locks.
        ImmutableList<SagaState> terminal =
            [SagaState.Completed, SagaState.Failed, SagaState.Rejected];
        Console.WriteLine($"ImmutableList terminal states = {terminal.Count}");

        // FrozenDictionary (.NET 8): perfect for static AllowedTransitions after startup.
        // Build once, read forever — faster lookups than Dictionary for large static maps.
        FrozenDictionary<SagaState, SagaState[]> frozenAllowed =
            new Dictionary<SagaState, SagaState[]>
            {
                [SagaState.Approved] = [SagaState.QueuedForFulfillment],
                [SagaState.Completed] = []
            }.ToFrozenDictionary();
        Console.WriteLine(
            $"FrozenDictionary Approved→Queued? " +
            $"{frozenAllowed[SagaState.Approved].Contains(SagaState.QueuedForFulfillment)}");
    }

    // =========================================================================
    // PART 12 — Concurrent collections & Channel
    // =========================================================================
    static void Part12_ConcurrentAndChannel()
    {
        Section("PART 12 — Concurrent collections & Channel");

        // ConcurrentDictionary: in-memory cache of recently processed MessageIds
        // (processed_messages table is the durable source of truth — no Redis in EntitleX).
        var processed = new ConcurrentDictionary<MessageId, byte>();
        var mid = MessageId.New();
        Console.WriteLine($"TryAdd processed MessageId → {processed.TryAdd(mid, 0)}");
        Console.WriteLine($"Duplicate TryAdd → {processed.TryAdd(mid, 0)} (false = idempotent hit)");

        // Channel<T>: in-process analogy for OutboxPublisher draining Pending rows.
        // Production uses Postgres FOR UPDATE SKIP LOCKED across multiple publisher instances.
        var channel = System.Threading.Channels.Channel.CreateBounded<OutboxMessage>(
            new System.Threading.Channels.BoundedChannelOptions(16)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        channel.Writer.TryWrite(new OutboxMessage(
            MessageId.New(), "FulfillmentCommand", "{}", OutboxStatus.Pending,
            DateTimeOffset.UtcNow, new EntitlementId("ent-partition-key")));
        channel.Reader.TryRead(out var claimed);
        Console.WriteLine(
            $"Channel claimed MessageType={claimed!.MessageType}; " +
            $"partition key analogy = entitlement id '{claimed.PartitionKey}' (Kafka key)");

        // ConcurrentQueue / ConcurrentBag: rarely needed if you already use Channel.
        var q = new ConcurrentQueue<CorrelationId>();
        q.Enqueue(CorrelationId.New());
        Console.WriteLine($"ConcurrentQueue TryDequeue → {q.TryDequeue(out _)}");
    }

    // =========================================================================
    // PART 13 — Specialized collections
    // =========================================================================
    static void Part13_Specialized()
    {
        Section("PART 13 — Specialized collections");

        // BitArray: compact flags for "item index fulfilled?" demos (≤1000 bits).
        var fulfilledFlags = new BitArray(1000);
        fulfilledFlags[0] = true;
        fulfilledFlags[999] = true;
        Console.WriteLine($"BitArray item0={fulfilledFlags[0]} item999={fulfilledFlags[999]}");

        // NameValueCollection / StringDictionary — legacy; prefer Dictionary<string,string>
        // for HTTP Idempotency-Key → retry_request_id maps in tests.
        var idempotency = new Dictionary<string, RequestId>(StringComparer.Ordinal)
        {
            ["client-key-1"] = RequestId.New()
        };
        Console.WriteLine($"Idempotency-Key map count = {idempotency.Count}");

        // Collection<T> / KeyedCollection — useful for ApprovalStage keyed by role.
        var stages = new ApprovalStageCollection
        {
            new("RenewalManager", ApprovalBand.RenewalManagerOnly),
            new("DealDesk", ApprovalBand.RenewalManagerThenDealDesk)
        };
        Console.WriteLine($"KeyedCollection DealDesk band = {stages["DealDesk"].Band}");
    }

    // =========================================================================
    // PART 14 — Performance cheat notes
    // =========================================================================
    static void Part14_PerformanceNotes()
    {
        Section("PART 14 — Performance cheat notes (collections lens)");

        // 1) Pre-size List to MaxItems (1000) when materializing selective-retry copies.
        // 2) HashSet for membership (FailedItemSelector) beats repeated List.Contains.
        // 3) Dictionary AllowedTransitions is O(1) state checks — never scan transition history.
        // 4) AsReadOnly is O(1) wrapper — ImmutableList copy is O(n); pick intentionally.
        // 5) FrozenDictionary for static maps after host startup (.NET 8).
        // 6) No Redis — ConcurrentDictionary is process-local only; durability = Postgres.
        // 7) EF bulk attach: AutoDetectChangesEnabled=false; Npgsql MaxBatchSize(100).
        // 8) Kafka partition key = entitlement id — Dictionary grouping by EntitlementId is fine.

        var sw = Stopwatch.StartNew();
        var set = Enumerable.Range(0, 1000).Select(_ => LogicalOperationId.New()).ToHashSet();
        var hits = 0;
        foreach (var id in set) if (set.Contains(id)) hits++;
        sw.Stop();
        Console.WriteLine($"1000 HashSet Contains hits={hits} in {sw.Elapsed.TotalMilliseconds:F2} ms");
    }

    // =========================================================================
    // PART 15 — Interview problems
    // =========================================================================
    static void Part15_InterviewProblems()
    {
        Section("PART 15 — Interview problems (EntitleX-flavored)");

        // --- Problem A: LRU cache for processed MessageIds (consumer idempotency) ---
        // Durable truth is processed_messages; LRU is an optional hot-path accelerator.
        var lru = new LruMessageCache(capacity: 3);
        var m1 = MessageId.New();
        var m2 = MessageId.New();
        var m3 = MessageId.New();
        var m4 = MessageId.New();
        lru.Put(m1); lru.Put(m2); lru.Put(m3);
        Console.WriteLine($"LRU contains m1? {lru.Contains(m1)}");
        lru.Put(m4); // evicts m1 (least recently used)
        Console.WriteLine($"After Put(m4), contains m1? {lru.Contains(m1)} (expect false)");
        Console.WriteLine($"Contains m2? {lru.Contains(m2)} (expect true)");

        // --- Problem B: Top-K pending approvals by age ---
        var board = new List<ApprovalCaseSummary>
        {
            new(Guid.NewGuid(), RequestId.New(), ApprovalBand.RenewalManagerOnly, DateTimeOffset.UtcNow.AddDays(-1), 1),
            new(Guid.NewGuid(), RequestId.New(), ApprovalBand.RenewalManagerThenDealDesk, DateTimeOffset.UtcNow.AddDays(-10), 10),
            new(Guid.NewGuid(), RequestId.New(), ApprovalBand.RenewalManagerOnly, DateTimeOffset.UtcNow.AddDays(-4), 4),
            new(Guid.NewGuid(), RequestId.New(), ApprovalBand.AutoApprove, DateTimeOffset.UtcNow.AddDays(-2), 2)
        };
        int k = 2;
        // Min-heap of size K on DaysWaiting — or OrderByDescending.Take(K) for K<<n.
        var topK = board.OrderByDescending(a => a.DaysWaiting).Take(k).ToList();
        Console.WriteLine($"Top-{k} aging approvals: {string.Join(", ", topK.Select(a => $"{a.DaysWaiting}d/{a.Band}"))}");
    }

    // =========================================================================
    // PART 16 — Interview cheat sheet
    // =========================================================================
    static void Part16_CheatSheet()
    {
        Section("PART 16 — Interview cheat sheet");

        Console.WriteLine("""
          EntitleX collection talking points:
          • Aggregates: private List + public AsReadOnly (Items, RetryHistory, stages).
          • Status machines: static Dictionary/FrozenDictionary of allowed transitions.
          • Selective retry: HashSet of failed LogicalOperationIds; never copy successes.
          • IDs: readonly record struct → great dictionary/set keys (value equality).
          • Outbox: DB SKIP LOCKED; Channel/ConcurrentQueue only as in-process analogies.
          • Approvals: PriorityQueue/keyset for aging; timeout → Escalated, never auto-approve.
          • Cap: MaxItems=1000; pre-size lists; EF AutoDetectChanges off + MaxBatchSize(100).
          • No Redis: ConcurrentDictionary is not a shared cache across workers.
          • Kafka key = EntitlementId; group/fan-out dictionaries keyed the same way.
          • Prefer IReadOnlyList / IReadOnlyDictionary on public surfaces.
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

// =============================================================================
 // Supporting demo types
 // =============================================================================

public sealed record RequestItemDemo(
    RequestItemId Id,
    EntitlementId EntitlementId,
    ProviderSystem Provider,
    LogicalOperationId LogicalOperationId,
    AttemptId AttemptId);

/// <summary>
 // Mini aggregate mirroring LicenseExtensionRequest item encapsulation.
 // Real: src/EntitleX.Domain/LicenseExtension/LicenseExtensionRequest.cs
 // </summary>
public sealed class LicenseExtensionRequestDemo
{
    private readonly List<RequestItemDemo> _items = [];
    private readonly int _maxItems;

    public LicenseExtensionRequestDemo(RequestId id, int maxItems)
    {
        Id = id;
        _maxItems = maxItems;
    }

    public RequestId Id { get; }
    public IReadOnlyList<RequestItemDemo> Items => _items.AsReadOnly();

    public void AddItem(
        EntitlementId entitlementId,
        ProviderSystem provider,
        LogicalOperationId logicalOperationId,
        AttemptId attemptId)
    {
        if (_items.Count >= _maxItems)
            throw new InvalidOperationException($"MaxItems={_maxItems} exceeded.");

        _items.Add(new RequestItemDemo(
            RequestItemId.New(), entitlementId, provider, logicalOperationId, attemptId));
    }
}

public sealed record ApprovalStageDemo(string Role, ApprovalBand Band);

public sealed class ApprovalStageCollection : KeyedCollection<string, ApprovalStageDemo>
{
    protected override string GetKeyForItem(ApprovalStageDemo item) => item.Role;
}

/// <summary>
 // Classic LRU via LinkedList + Dictionary — analogy for hot processed_messages cache.
 // Durable idempotency remains in Postgres (no Redis).
 // </summary>
public sealed class LruMessageCache
{
    private readonly int _capacity;
    private readonly Dictionary<MessageId, LinkedListNode<MessageId>> _map = new();
    private readonly LinkedList<MessageId> _order = new();

    public LruMessageCache(int capacity) => _capacity = capacity;

    public bool Contains(MessageId id)
    {
        if (!_map.TryGetValue(id, out var node)) return false;
        _order.Remove(node);
        _order.AddFirst(node);
        return true;
    }

    public void Put(MessageId id)
    {
        if (Contains(id)) return;
        if (_map.Count >= _capacity)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value);
        }
        var node = _order.AddFirst(id);
        _map[id] = node;
    }
}
