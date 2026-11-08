// =============================================================================
 // EntitleX .NET DATA STRUCTURES GUIDE
 // Classic data-structure interview material, rewritten so every structure
 // solves an EntitleX control-plane problem (approvals, sagas, outbox, retry).
 //
 // Target: .NET 8  |  No external NuGet packages required.
 //
 // Real paths referenced:
 //   src/EntitleX.Domain/LicenseExtension/LicenseExtensionRequest.cs
 //   src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
 //   src/EntitleX.Domain/Approval/ApprovalCase.cs
 //   src/EntitleX.Domain/Approval/ApprovalStage.cs
 //   src/EntitleX.Domain/LicenseExtension/FailedItemSelector.cs
 //   src/EntitleX.BuildingBlocks/Ids/*.cs
 //   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
 //   docs/saga-workflow.md  |  docs/idempotency.md
 //
 // TABLE OF CONTENTS
 //   PART 1  — Arrays (provider fan-out, fixed bands)
 //   PART 2  — List<T> (aggregate items ≤1000)
 //   PART 3  — LinkedList (LRU / rare control-plane uses)
 //   PART 4  — Stack & Queue (draft undo / outbox drain)
 //   PART 5  — Heap / PriorityQueue (approval aging, poison backoff)
 //   PART 6  — Hashing (HashSet / Dictionary / ApprovedScopeHasher)
 //   PART 7  — Sorted structures (SortedSet / SortedDictionary / SortedList)
 //   PART 8  — Trees (BST mental model ↔ Postgres B-tree indexes)
 //   PART 9  — Trie (entitlement SKU / id prefix search toy)
 //   PART 10 — Graphs (approval stage DAG / saga transition digraph)
 //   PART 11 — Union-Find / misc (retry lineage components)
 //   PART 12 — Bloom filter sketch (processed MessageId — educational)
 //   PART 13 — Complexity cheat table (EntitleX workloads)
 //   PART 14 — Interview problems
 //   PART 15 — Interview cheat sheet
 // =============================================================================

#nullable enable
using System.Diagnostics;
using System.Text;

namespace EntitleX.Guides.DataStructures;

// -----------------------------------------------------------------------------
 // Simulated domain vocabulary
 // -----------------------------------------------------------------------------

public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct RequestItemId(Guid Value)
{
    public static RequestItemId New() => new(Guid.NewGuid());
}

public readonly record struct LogicalOperationId(Guid Value)
{
    public static LogicalOperationId New() => new(Guid.NewGuid());
}

public readonly record struct AttemptId(Guid Value)
{
    public static AttemptId New() => new(Guid.NewGuid());
}

public readonly record struct EntitlementId(string Value)
{
    public override string ToString() => Value;
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

public enum ApprovalBand
{
    AutoApprove, RenewalManagerOnly, RenewalManagerThenDealDesk, Unsupported
}

public enum SagaState
{
    Requested, PendingRenewalManagerApproval, PendingDealDeskApproval, Approved,
    QueuedForFulfillment, Processing, PartiallyCompleted, Completed, Failed,
    ReconciliationRequired, Escalated, Rejected
}

public enum ApprovalStageRole { RenewalManager, DealDesk }

public sealed record RequestItem(
    RequestItemId Id,
    EntitlementId EntitlementId,
    ProviderSystem Provider,
    LogicalOperationId LogicalOperationId,
    AttemptId AttemptId);

public sealed record ApprovalCaseNode(
    Guid Id,
    RequestId RequestId,
    ApprovalBand Band,
    DateTimeOffset CreatedAtUtc,
    int DaysWaiting);

// =============================================================================
public static class Program
{
    public static void Main()
    {
        Console.WriteLine("=== EntitleX Data Structures Guide (.NET 8) ===\n");

        Part1_Arrays();
        Part2_Lists();
        Part3_LinkedList();
        Part4_StackAndQueue();
        Part5_HeapPriorityQueue();
        Part6_Hashing();
        Part7_SortedStructures();
        Part8_Trees();
        Part9_Trie();
        Part10_Graphs();
        Part11_UnionFindAndMisc();
        Part12_BloomSketch();
        Part13_ComplexityTable();
        Part14_InterviewProblems();
        Part15_CheatSheet();

        Console.WriteLine("\n=== Done. Map every structure back to a control-plane invariant. ===");
    }

    // =========================================================================
    // PART 1 — Arrays
    // =========================================================================
    static void Part1_Arrays()
    {
        Section("PART 1 — Arrays");

        // Fixed provider set — never grows at runtime.
        ProviderSystem[] providers =
        [
            ProviderSystem.CloudShield,
            ProviderSystem.NetworkGuard
        ];
        Console.WriteLine($"Provider fan-out array: {string.Join(", ", providers)}");

        // ApprovalBand thresholds as parallel arrays (toy) — real rules live in ApprovalRules.
        int[] maxDays = [30, 90, 365, int.MaxValue];
        ApprovalBand[] bands =
        [
            ApprovalBand.AutoApprove,
            ApprovalBand.RenewalManagerOnly,
            ApprovalBand.RenewalManagerThenDealDesk,
            ApprovalBand.Unsupported
        ];
        int extensionDays = 120;
        ApprovalBand resolved = ApprovalBand.Unsupported;
        for (int i = 0; i < maxDays.Length; i++)
        {
            if (extensionDays <= maxDays[i]) { resolved = bands[i]; break; }
        }
        Console.WriteLine($"extensionDays={extensionDays} → ApprovalBand.{resolved}");

        // Multidimensional: rare. Prefer array of RequestItem / Dictionary.
        Console.WriteLine("Prefer jagged List collections over[,] for saga item matrices.");
    }

    // =========================================================================
    // PART 2 — List<T>
    // =========================================================================
    static void Part2_Lists()
    {
        Section("PART 2 — List<T> (aggregate items ≤1000)");

        // LicenseExtensionRequest: private List + AsReadOnly; MaxItems = 1000.
        var items = new List<RequestItem>(capacity: 1000);
        for (int i = 0; i < 5; i++)
        {
            items.Add(new RequestItem(
                RequestItemId.New(),
                new EntitlementId($"ent-cloudshield-{i:D3}"),
                ProviderSystem.CloudShield,
                LogicalOperationId.New(),
                AttemptId.New()));
        }
        IReadOnlyList<RequestItem> exposed = items.AsReadOnly();
        Console.WriteLine($"List Capacity={items.Capacity} Count={exposed.Count}");

        // Selective retry copies *failed* items only into a new List on a child request.
        var failedOnly = new List<RequestItem>(capacity: 16);
        failedOnly.Add(items[1]);
        failedOnly.Add(items[3]);
        Console.WriteLine($"Selective retry materializes {failedOnly.Count} failed items (LogicalOperationId preserved)");

        // Amortized O(1) Add; Insert mid-list is O(n) — don't insert at 0 for outbox ordering.
    }

    // =========================================================================
    // PART 3 — LinkedList
    // =========================================================================
    static void Part3_LinkedList()
    {
        Section("PART 3 — LinkedList");

        // Real EntitleX persistence is relational — LinkedList is an in-memory tool.
        // Classic: LRU node order for processed MessageId hot cache (see Collections guide).
        var order = new LinkedList<MessageId>();
        var a = order.AddFirst(MessageId.New());
        order.AddLast(MessageId.New());
        order.Remove(a);
        Console.WriteLine($"LinkedList count after LRU-like remove = {order.Count}");
        Console.WriteLine("Do not model ApprovalStage sequences as LinkedList in EF — use ordered List + role.");
    }

    // =========================================================================
    // PART 4 — Stack & Queue
    // =========================================================================
    static void Part4_StackAndQueue()
    {
        Section("PART 4 — Stack & Queue");

        // Stack: draft UI undo OR depth-first traversal of saga transition history for debugging.
        var dfs = new Stack<SagaState>();
        dfs.Push(SagaState.Requested);
        dfs.Push(SagaState.Approved);
        dfs.Push(SagaState.QueuedForFulfillment);
        Console.WriteLine($"DFS/Stack peek = {dfs.Peek()}");

        // Queue: BFS through approval stage DAG; also local outbox FIFO analogy.
        var bfs = new Queue<ApprovalStageRole>();
        bfs.Enqueue(ApprovalStageRole.RenewalManager);
        bfs.Enqueue(ApprovalStageRole.DealDesk);
        Console.WriteLine($"BFS/Queue dequeue = {bfs.Dequeue()} (RM before Deal Desk — band invariant)");

        // Forward recovery reminder: Stack is NOT used to "pop" successful extensions away.
        Console.WriteLine("No compensating stack of provider undos — successes stay applied.");
    }

    // =========================================================================
    // PART 5 — Heap / PriorityQueue
    // =========================================================================
    static void Part5_HeapPriorityQueue()
    {
        Section("PART 5 — Heap / PriorityQueue");

        // Min-heap by CreatedAtUtc → oldest pending ApprovalCase first (ops board).
        var heap = new PriorityQueue<ApprovalCaseNode, long>();
        var now = DateTimeOffset.UtcNow;
        void Offer(int days, ApprovalBand band)
        {
            var node = new ApprovalCaseNode(Guid.NewGuid(), RequestId.New(), band, now.AddDays(-days), days);
            heap.Enqueue(node, node.CreatedAtUtc.UtcTicks);
        }
        Offer(3, ApprovalBand.RenewalManagerOnly);
        Offer(11, ApprovalBand.RenewalManagerThenDealDesk);
        Offer(1, ApprovalBand.RenewalManagerOnly);

        var oldest = heap.Dequeue();
        Console.WriteLine($"Oldest approval waited {oldest.DaysWaiting}d band={oldest.Band}");
        Console.WriteLine("Timeout path → Escalated (never auto-approve).");

        // Second use: outbox Poison / retry AvailableAtUtc scheduling (exponential backoff).
        var backoff = new PriorityQueue<MessageId, long>();
        var msg = MessageId.New();
        var availableAt = DateTimeOffset.UtcNow.AddSeconds(30).UtcTicks;
        backoff.Enqueue(msg, availableAt);
        Console.WriteLine($"Backoff PriorityQueue next MessageId={msg}");
    }

    // =========================================================================
    // PART 6 — Hashing
    // =========================================================================
    static void Part6_Hashing()
    {
        Section("PART 6 — Hashing");

        // Dictionary: AllowedTransitions for SagaState / LicenseExtensionRequestStatus.
        var allowed = new Dictionary<SagaState, SagaState[]>
        {
            [SagaState.Approved] = [SagaState.QueuedForFulfillment],
            [SagaState.Processing] =
            [
                SagaState.PartiallyCompleted,
                SagaState.Completed,
                SagaState.Failed,
                SagaState.ReconciliationRequired
            ],
            [SagaState.Completed] = []
        };
        Console.WriteLine(
            $"Hash map transition Processing→Failed? " +
            $"{allowed[SagaState.Processing].Contains(SagaState.Failed)}");

        // HashSet: FailedItemSelector membership on LogicalOperationId.
        var failed = new HashSet<LogicalOperationId>
        {
            LogicalOperationId.New(),
            LogicalOperationId.New()
        };
        var probe = failed.First();
        Console.WriteLine($"HashSet failed Contains = {failed.Contains(probe)}");

        // ApprovedScopeHasher analogy — stable hash of approved entitlement set.
        var scope = new[]
        {
            new EntitlementId("ent-a"),
            new EntitlementId("ent-b"),
            new EntitlementId("ent-c")
        };
        string scopeHash = FakeApprovedScopeHash(scope);
        Console.WriteLine($"ApprovedScopeHash toy = {scopeHash}");
        Console.WriteLine("Selective retry reuses approvals when parent ApprovedScopeHash still valid.");
    }

    static string FakeApprovedScopeHash(IEnumerable<EntitlementId> entitlements)
    {
        // Interview: sort then hash for order-insensitivity.
        var sorted = entitlements.Select(e => e.Value).OrderBy(v => v, StringComparer.Ordinal);
        var joined = string.Join('|', sorted);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }

    // =========================================================================
    // PART 7 — Sorted structures
    // =========================================================================
    static void Part7_SortedStructures()
    {
        Section("PART 7 — Sorted structures");

        var entitlements = new SortedSet<string>(StringComparer.Ordinal)
        {
            "ent-networkguard-002",
            "ent-cloudshield-010",
            "ent-cloudshield-001"
        };
        Console.WriteLine($"SortedSet min entitlement = {entitlements.Min}");

        var bandCounts = new SortedDictionary<ApprovalBand, int>
        {
            [ApprovalBand.AutoApprove] = 80,
            [ApprovalBand.RenewalManagerOnly] = 15,
            [ApprovalBand.RenewalManagerThenDealDesk] = 4,
            [ApprovalBand.Unsupported] = 1
        };
        foreach (var kv in bandCounts)
            Console.WriteLine($"  {kv.Key}: {kv.Value}");

        // SortedList: good for rare inserts + frequent index access of small maps.
        var providerCodes = new SortedList<string, ProviderSystem>
        {
            ["CloudShield"] = ProviderSystem.CloudShield,
            ["NetworkGuard"] = ProviderSystem.NetworkGuard
        };
        Console.WriteLine($"SortedList GetByIndex(0) = {providerCodes.GetKeyAtIndex(0)}");
    }

    // =========================================================================
    // PART 8 — Trees
    // =========================================================================
    static void Part8_Trees()
    {
        Section("PART 8 — Trees (BST mental model ↔ indexes)");

        // In-memory BST teaching model for "find ApprovalCase by CreatedAtUtc".
        // Production: Postgres B-tree on (created_at) / keyset (occurred_at, id).
        var bst = new BstApprovalIndex();
        var now = DateTimeOffset.UtcNow;
        bst.Insert(new ApprovalCaseNode(Guid.NewGuid(), RequestId.New(), ApprovalBand.RenewalManagerOnly, now.AddDays(-5), 5));
        bst.Insert(new ApprovalCaseNode(Guid.NewGuid(), RequestId.New(), ApprovalBand.RenewalManagerThenDealDesk, now.AddDays(-2), 2));
        bst.Insert(new ApprovalCaseNode(Guid.NewGuid(), RequestId.New(), ApprovalBand.RenewalManagerOnly, now.AddDays(-9), 9));

        Console.WriteLine("In-order (oldest→newest) walk:");
        foreach (var n in bst.InOrder())
            Console.WriteLine($"  {n.DaysWaiting}d {n.Band}");

        Console.WriteLine("Interview bridge: explain why ix_outbox_messages_status_available is a B-tree + SKIP LOCKED.");
    }

    // =========================================================================
    // PART 9 — Trie
    // =========================================================================
    static void Part9_Trie()
    {
        Section("PART 9 — Trie (entitlement SKU / id prefix search)");

        // Toy ops console: typeahead for entitlement ids / SKU prefixes.
        // Not in production hot path — useful interview toy with EntitleX vocabulary.
        var trie = new EntitlementPrefixTrie();
        trie.Insert("ent-cloudshield-001");
        trie.Insert("ent-cloudshield-002");
        trie.Insert("ent-cloudshield-010");
        trie.Insert("ent-networkguard-014");
        trie.Insert("sku-CS-ENTERPRISE-1Y");
        trie.Insert("sku-CS-ENTERPRISE-3Y");
        trie.Insert("sku-NG-FIREWALL-1Y");

        var hits = trie.StartsWith("ent-cloudshield-00");
        Console.WriteLine($"Prefix 'ent-cloudshield-00' → {string.Join(", ", hits)}");

        var skuHits = trie.StartsWith("sku-CS-ENTERPRISE");
        Console.WriteLine($"Prefix 'sku-CS-ENTERPRISE' → {string.Join(", ", skuHits)}");
    }

    // =========================================================================
    // PART 10 — Graphs
    // =========================================================================
    static void Part10_Graphs()
    {
        Section("PART 10 — Graphs (approval DAG / saga digraph)");

        // Approval stage DAG for RenewalManagerThenDealDesk:
        //   RM → DealDesk → Approved
        // AutoApprove: empty path → Approved
        var approvalDag = new Dictionary<string, string[]>
        {
            ["Start"] = ["RenewalManager"],
            ["RenewalManager"] = ["DealDesk"],
            ["DealDesk"] = ["Approved"],
            ["Approved"] = []
        };
        Console.WriteLine("Approval DAG topological hint: " + string.Join(" → ", TopoHint(approvalDag)));

        // Saga AllowedTransitions is a directed graph — detect illegal edges fast via adjacency map.
        var sagaGraph = new Dictionary<SagaState, SagaState[]>
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
            [SagaState.PendingDealDeskApproval] =
            [
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

        bool Edge(SagaState from, SagaState to) =>
            sagaGraph.TryGetValue(from, out var next) && next.Contains(to);

        Console.WriteLine($"Saga edge Approved→QueuedForFulfillment? {Edge(SagaState.Approved, SagaState.QueuedForFulfillment)}");
        Console.WriteLine($"Saga edge Completed→Processing? {Edge(SagaState.Completed, SagaState.Processing)}");

        // BFS reachability: can Escalated return to fulfillment path? (via AllowedTransitions in real saga)
        Console.WriteLine("Graph talk track: adjacency Dictionary = O(1) illegal-transition checks.");
    }

    static IEnumerable<string> TopoHint(Dictionary<string, string[]> dag)
    {
        // Tiny Kahn for the fixed approval DAG demo.
        var indeg = dag.Keys.ToDictionary(k => k, _ => 0);
        foreach (var toList in dag.Values)
        foreach (var to in toList)
            if (indeg.ContainsKey(to)) indeg[to]++;
            else indeg[to] = 1;

        var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            yield return n;
            if (!dag.TryGetValue(n, out var next)) continue;
            foreach (var v in next)
            {
                indeg[v]--;
                if (indeg[v] == 0) q.Enqueue(v);
            }
        }
    }

    // =========================================================================
    // PART 11 — Union-Find / misc
    // =========================================================================
    static void Part11_UnionFindAndMisc()
    {
        Section("PART 11 — Union-Find / misc (retry lineage)");

        // Union-Find groups selective-retry generations that share LogicalOperationId lineages
        // for ops analytics ("show me the connected component of this entitlement failure").
        var uf = new UnionFind(8);
        // Requests 0-2 are one ParentId chain; 3-4 another.
        uf.Union(0, 1);
        uf.Union(1, 2);
        uf.Union(3, 4);
        Console.WriteLine($"Same lineage 0 & 2? {uf.Find(0) == uf.Find(2)}");
        Console.WriteLine($"Same lineage 0 & 3? {uf.Find(0) == uf.Find(3)}");

        // Bitset: which of 1000 items succeeded?
        var successBits = new bool[1000];
        successBits[0] = true;
        successBits[999] = true;
        int successCount = successBits.Count(b => b);
        Console.WriteLine($"Bitset successCount={successCount} (forward recovery keeps these LogicalOperationIds)");
    }

    // =========================================================================
    // PART 12 — Bloom filter sketch
    // =========================================================================
    static void Part12_BloomSketch()
    {
        Section("PART 12 — Bloom filter sketch (educational)");

        // NOT used in EntitleX production (Postgres processed_messages instead).
        // Interview: approximate "have we seen MessageId?" with false positives, no false negatives.
        var bloom = new TinyBloom(sizeBits: 2048, hashCount: 3);
        var seen = MessageId.New();
        var other = MessageId.New();
        bloom.Add(seen.Value);
        Console.WriteLine($"Bloom maybe-contains seen? {bloom.MightContain(seen.Value)}");
        Console.WriteLine($"Bloom maybe-contains other? {bloom.MightContain(other.Value)} (probably false)");
        Console.WriteLine("Production choice: durable unique MessageId / LogicalOperationId indexes.");
    }

    // =========================================================================
    // PART 13 — Complexity table
    // =========================================================================
    static void Part13_ComplexityTable()
    {
        Section("PART 13 — Complexity cheat table");

        Console.WriteLine("""
          Structure              Hot EntitleX use                         Avg / notes
          --------------------   ---------------------------------------  --------------------------
          List<T>                Request items (≤1000)                    Add amort. O(1)
          IReadOnlyList view     Aggregate encapsulation                  O(1) wrap
          Dictionary             AllowedTransitions / handlers            O(1)
          HashSet                Failed LogicalOperationId select         O(1)
          PriorityQueue          Approval aging / backoff                 O(log n)
          Queue                  BFS approval DAG / local drain           O(1)
          Stack                  DFS transition debug / undo              O(1)
          SortedSet              Deterministic entitlement listing        O(log n)
          Trie                   Ops prefix search toy                    O(k)
          Graph (adj map)        Saga / approval legality                 O(1) edge check
          B-tree (Postgres)      Outbox claim index, audit keyset         O(log n) I/O
          """);
    }

    // =========================================================================
    // PART 14 — Interview problems
    // =========================================================================
    static void Part14_InterviewProblems()
    {
        Section("PART 14 — Interview problems");

        // Problem 1: Detect cycle in a mistaken saga transition config.
        var bad = new Dictionary<SagaState, SagaState[]>
        {
            [SagaState.Processing] = [SagaState.PartiallyCompleted],
            [SagaState.PartiallyCompleted] = [SagaState.Processing] // intentional cycle for demo
        };
        Console.WriteLine($"Cycle in toy graph? {HasCycle(bad)}");

        // Problem 2: Top-K entitlements by failure count (HashMap + heap).
        var failures = new Dictionary<EntitlementId, int>
        {
            [new EntitlementId("ent-1")] = 4,
            [new EntitlementId("ent-2")] = 1,
            [new EntitlementId("ent-3")] = 9,
            [new EntitlementId("ent-4")] = 3
        };
        var top2 = failures.OrderByDescending(kv => kv.Value).Take(2)
            .Select(kv => $"{kv.Key}:{kv.Value}");
        Console.WriteLine($"Top-2 failing entitlements: {string.Join(", ", top2)}");

        // Problem 3: Validate ParentId chain length for selective retries.
        var chain = new List<RequestId> { RequestId.New(), RequestId.New(), RequestId.New() };
        Console.WriteLine($"Retry lineage depth = {chain.Count} (ParentId linked list conceptually)");
    }

    static bool HasCycle(Dictionary<SagaState, SagaState[]> graph)
    {
        var visiting = new HashSet<SagaState>();
        var visited = new HashSet<SagaState>();
        bool Dfs(SagaState n)
        {
            if (visiting.Contains(n)) return true;
            if (visited.Contains(n)) return false;
            visiting.Add(n);
            if (graph.TryGetValue(n, out var next))
            {
                foreach (var v in next)
                    if (Dfs(v)) return true;
            }
            visiting.Remove(n);
            visited.Add(n);
            return false;
        }
        foreach (var k in graph.Keys)
            if (Dfs(k)) return true;
        return false;
    }

    // =========================================================================
    // PART 15 — Cheat sheet
    // =========================================================================
    static void Part15_CheatSheet()
    {
        Section("PART 15 — Interview cheat sheet");

        Console.WriteLine("""
          Map structures → EntitleX invariants:
          • List + AsReadOnly → LicenseExtensionRequest.Items / RetryHistory
          • Dictionary transitions → SagaState & request status machines
          • HashSet → FailedItemSelector (never include successes)
          • PriorityQueue → aging ApprovalCase / outbox backoff
          • Graph/DAG → RM→DealDesk sequencing; saga digraph
          • Trie → entitlement/SKU prefix UX toy
          • readonly record struct IDs → hash keys (LogicalOperationId, AttemptId, …)
          • Postgres B-tree + SKIP LOCKED → real outbox “queue”
          • No Redis; no compensating stack for provider undos (forward recovery)
          • MaxItems 1000; design algorithms that stay friendly at that bound
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
 // Supporting structures
 // =============================================================================

sealed class BstNode
{
    public required ApprovalCaseNode Value;
    public BstNode? Left;
    public BstNode? Right;
}

sealed class BstApprovalIndex
{
    private BstNode? _root;

    public void Insert(ApprovalCaseNode value) => _root = Insert(_root, value);

    static BstNode Insert(BstNode? node, ApprovalCaseNode value)
    {
        if (node is null) return new BstNode { Value = value };
        if (value.CreatedAtUtc < node.Value.CreatedAtUtc)
            node.Left = Insert(node.Left, value);
        else
            node.Right = Insert(node.Right, value);
        return node;
    }

    public IEnumerable<ApprovalCaseNode> InOrder() => Walk(_root);

    static IEnumerable<ApprovalCaseNode> Walk(BstNode? n)
    {
        if (n is null) yield break;
        foreach (var x in Walk(n.Left)) yield return x;
        yield return n.Value;
        foreach (var x in Walk(n.Right)) yield return x;
    }
}

sealed class TrieNode
{
    public Dictionary<char, TrieNode> Kids { get; } = new();
    public bool IsEnd { get; set; }
}

/// <summary>Prefix trie for entitlement id / SKU typeahead demos.</summary>
sealed class EntitlementPrefixTrie
{
    private readonly TrieNode _root = new();

    public void Insert(string value)
    {
        var n = _root;
        foreach (var ch in value)
        {
            if (!n.Kids.TryGetValue(ch, out var next))
            {
                next = new TrieNode();
                n.Kids[ch] = next;
            }
            n = next;
        }
        n.IsEnd = true;
    }

    public List<string> StartsWith(string prefix)
    {
        var n = _root;
        foreach (var ch in prefix)
        {
            if (!n.Kids.TryGetValue(ch, out var next)) return [];
            n = next;
        }
        var results = new List<string>();
        Dfs(n, new StringBuilder(prefix), results);
        return results;
    }

    static void Dfs(TrieNode n, StringBuilder sb, List<string> results)
    {
        if (n.IsEnd) results.Add(sb.ToString());
        foreach (var (ch, child) in n.Kids)
        {
            sb.Append(ch);
            Dfs(child, sb, results);
            sb.Length--;
        }
    }
}

sealed class UnionFind
{
    private readonly int[] _parent;
    private readonly int[] _rank;

    public UnionFind(int n)
    {
        _parent = Enumerable.Range(0, n).ToArray();
        _rank = new int[n];
    }

    public int Find(int x) => _parent[x] == x ? x : _parent[x] = Find(_parent[x]);

    public void Union(int a, int b)
    {
        var ra = Find(a);
        var rb = Find(b);
        if (ra == rb) return;
        if (_rank[ra] < _rank[rb]) _parent[ra] = rb;
        else if (_rank[ra] > _rank[rb]) _parent[rb] = ra;
        else { _parent[rb] = ra; _rank[ra]++; }
    }
}

/// <summary>Tiny Bloom filter for MessageId educational demos only.</summary>
sealed class TinyBloom
{
    private readonly bool[] _bits;
    private readonly int _hashCount;

    public TinyBloom(int sizeBits, int hashCount)
    {
        _bits = new bool[sizeBits];
        _hashCount = hashCount;
    }

    public void Add(Guid id)
    {
        foreach (var i in Indexes(id)) _bits[i] = true;
    }

    public bool MightContain(Guid id)
    {
        foreach (var i in Indexes(id))
            if (!_bits[i]) return false;
        return true;
    }

    IEnumerable<int> Indexes(Guid id)
    {
        var h1 = id.GetHashCode();
        var bytes = id.ToByteArray();
        var h2 = BitConverter.ToInt32(bytes, 4);
        for (int i = 0; i < _hashCount; i++)
        {
            var combined = h1 + i * h2;
            yield return Math.Abs(combined % _bits.Length);
        }
    }
}
