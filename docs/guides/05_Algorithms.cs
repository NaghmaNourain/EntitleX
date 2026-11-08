// =============================================================================
// EntitleX Educational Guide 05 — Algorithms for License-Extension Control Planes
// =============================================================================
//
// HOW TO USE THIS FILE
// --------------------
// 1. This is a STANDALONE console-oriented teaching artifact.
// 2. Inline domain types live in EntitleX.Guides.Algorithms.Domain so you do NOT
//    need to reference src/EntitleX.Domain to compile a toy demo.
// 3. Every algorithm is framed as an EntitleX-shaped problem: day-band approval,
//    saga stage ordering, entitlement graphs, reminder scheduling, scope hashing,
//    selective retry lineage (LogicalOperationId / AttemptId / ParentId), etc.
//
// REAL REPO ANCHORS (read these alongside the demos)
// --------------------------------------------------
//   src/EntitleX.Domain/Approval/ApprovalRules.cs
//   src/EntitleX.Domain/Approval/ApprovedScopeHasher.cs
//   src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
//   src/EntitleX.Domain/LicenseExtension/FailedItemSelector.cs
//   src/EntitleX.Infrastructure/Outbox/OutboxClaimer.cs
//   docs/saga-workflow.md
//   docs/performance.md
//
// TABLE OF CONTENTS
// -----------------
//   PART 0  — Shared domain stubs + DemoHost
//   PART 1  — Algorithmic basics (complexity, invariants)
//   PART 2  — Searching (linear, binary, lower_bound aging)
//   PART 3  — Sorting (band priority, merge, quick)
//   PART 4  — Array / two-pointer / sliding-window techniques
//   PART 5  — Backtracking (subset selective-retry combinations)
//   PART 6  — Divide & Conquer (split CloudShield vs NetworkGuard batches)
//   PART 7  — Dynamic Programming (reminder schedule cost)
//   PART 8  — Greedy (day-band evaluation, earliest-deadline first)
//   PART 9  — Graphs (BFS entitlement adjacency, topo approval stages)
//   PART 10 — Trees (segment/fenwick analogies for aging buckets)
//   PART 11 — Strings (rolling hash ≈ ApprovedScopeHasher)
//   PART 12 — Math & bits (bitset eligibility, modular facts)
//   PART 13 — Interview cheat sheet
//
// =============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EntitleX.Guides.Algorithms
{

// =============================================================================
// PART 0 — Shared domain stubs (mirrors real types lightly)
// =============================================================================
namespace Domain
{
    /// <summary>Mirrors src/EntitleX.Domain/Approval/ApprovalBand.cs</summary>
    public enum ApprovalBand
    {
        AutoApprove = 0,                 // 1–30
        RenewalManagerOnly = 1,          // 31–90
        RenewalManagerThenDealDesk = 2,  // 91–365
        Unsupported = 3                  // >365
    }

    public enum ProviderSystem { CloudShield = 0, NetworkGuard = 1 }

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

    /// <summary>Inline stand-in for a LicenseExtensionRequestItem row.</summary>
    public sealed record ExtensionItem(
        Guid LogicalOperationId,
        Guid AttemptId,
        Guid? ParentId,
        string EntitlementId,
        ProviderSystem Provider,
        DateTimeOffset CurrentExpiryUtc,
        DateTimeOffset RequestedExpiryUtc)
    {
        public int ExtensionDays =>
            Math.Max(0, (int)Math.Ceiling((RequestedExpiryUtc - CurrentExpiryUtc).TotalDays));
    }

    public sealed record PendingApprovalCase(
        Guid ApprovalCaseId,
        DateTimeOffset CreatedAtUtc,
        string RequiredRole, // RenewalManager | DealDeskManager
        int MaxExtensionDays,
        ApprovalBand Band);

    public sealed record EntitlementNode(string Id, ProviderSystem Provider, DateTimeOffset ExpiryUtc);

    /// <summary>
    /// Day-band policy — same thresholds as
    /// src/EntitleX.Domain/Approval/ApprovalRules.cs
    /// </summary>
    public static class ApprovalRulesDemo
    {
        public const int MinSupportedDays = 1;
        public const int AutoApproveMaxDays = 30;
        public const int RenewalManagerMaxDays = 90;
        public const int MaxSupportedDays = 365;

        public static ApprovalBand EvaluateBand(int maxExtensionDays)
        {
            if (maxExtensionDays < MinSupportedDays) throw new ArgumentOutOfRangeException(nameof(maxExtensionDays));
            if (maxExtensionDays <= AutoApproveMaxDays) return ApprovalBand.AutoApprove;
            if (maxExtensionDays <= RenewalManagerMaxDays) return ApprovalBand.RenewalManagerOnly;
            if (maxExtensionDays <= MaxSupportedDays) return ApprovalBand.RenewalManagerThenDealDesk;
            return ApprovalBand.Unsupported;
        }

        public static IReadOnlyList<string> RequiredStages(ApprovalBand band) => band switch
        {
            ApprovalBand.AutoApprove => Array.Empty<string>(),
            ApprovalBand.RenewalManagerOnly => new[] { "RenewalManager" },
            ApprovalBand.RenewalManagerThenDealDesk => new[] { "RenewalManager", "DealDeskManager" },
            _ => Array.Empty<string>()
        };
    }
}

public static class DemoHost
{
    public static void Main()
    {
        Console.WriteLine("=== EntitleX Algorithms Guide Demos ===\n");
        Part1_Basics.Run();
        Part2_Search.Run();
        Part3_Sorting.Run();
        Part4_ArrayTechniques.Run();
        Part5_Backtracking.Run();
        Part6_DivideAndConquer.Run();
        Part7_DP.Run();
        Part8_Greedy.Run();
        Part9_Graphs.Run();
        Part10_Trees.Run();
        Part11_Strings.Run();
        Part12_MathBits.Run();
        Part13_InterviewNotes.Print();
    }
}

// =============================================================================
// PART 1 — Algorithmic basics
// =============================================================================
/// <summary>
/// INTERVIEW NOTE: Always state input size, hot path, and whether the algorithm
/// runs in-request (API latency budget) or offline (worker/lambda).
/// EntitleX API must stay O(n log n) or better for ≤1000 items per request.
/// See docs/performance.md and MaxBatchSize(100) in Infrastructure DI.
/// </summary>
public static class Part1_Basics
{
    public static void Run()
    {
        Console.WriteLine("-- PART 1: Basics / complexity --");

        // Invariant: controlling band uses MAX extension days across items
        // (ApprovalRules.ComputeMaxExtensionDays).
        var items = SampleItems();
        var maxDays = items.Max(i => i.ExtensionDays);
        var band = Domain.ApprovalRulesDemo.EvaluateBand(maxDays);
        Console.WriteLine($"  maxDays={maxDays} → band={band}");

        // Complexity mental model for a 1000-item create:
        //   O(n) scan for max days
        //   O(n log n) sort for ApprovedScopeHasher item order
        //   O(n) persist + outbox enqueue (batched ≤100 commands/round-trip)
        ExplainBigO();
        Console.WriteLine();
    }

    private static void ExplainBigO()
    {
        // Table: operation → typical complexity in EntitleX hot paths
        var rows = new (string Op, string BigO, string Where)[]
        {
            ("EvaluateBand(maxDays)", "O(1)", "ApprovalRules.cs"),
            ("ComputeMaxExtensionDays", "O(n)", "ApprovalRules.cs"),
            ("ApprovedScopeHasher", "O(n log n)", "sort + SHA256"),
            ("FailedItemSelector", "O(n+m)", "hash-set of failed LogicalOperationIds"),
            ("OutboxClaimer batch", "O(b)", "FOR UPDATE SKIP LOCKED LIMIT b"),
            ("Pending keyset page", "O(log N + k)", "index seek + take k"),
        };
        foreach (var (op, bigO, where) in rows)
            Console.WriteLine($"  {op,-28} {bigO,-12} // {where}");
    }

    internal static List<Domain.ExtensionItem> SampleItems()
    {
        var now = DateTimeOffset.Parse("2026-11-08T00:00:00Z");
        return new List<Domain.ExtensionItem>
        {
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.NewGuid(), null,
                "CS-ENT-001", Domain.ProviderSystem.CloudShield, now, now.AddDays(14)),
            new(Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.NewGuid(), null,
                "NG-ENT-009", Domain.ProviderSystem.NetworkGuard, now, now.AddDays(45)),
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), Guid.NewGuid(), null,
                "CS-ENT-044", Domain.ProviderSystem.CloudShield, now, now.AddDays(120)),
        };
    }
}

// =============================================================================
// PART 2 — Searching
// =============================================================================
/// <summary>
/// Binary search on sorted approval aging: find the first pending case older
/// than a SLA threshold (e.g., escalate if CreatedAtUtc + 48h &lt; now).
/// Real code: ApprovalReminderPolicy + lambdas/EntitleX.ApprovalReminder.
/// </summary>
public static class Part2_Search
{
    public static void Run()
    {
        Console.WriteLine("-- PART 2: Searching --");

        var cases = BuildSortedPendingCases();
        // Linear scan — fine for tiny in-memory demos; prefer indexed queries in Postgres.
        var aging = cases.Where(c => (DateTimeOffset.UtcNow - c.CreatedAtUtc).TotalHours >= 48).ToList();
        Console.WriteLine($"  linear aging count={aging.Count}");

        // Binary search lower_bound: first case with CreatedAtUtc >= cutoff
        var cutoff = DateTimeOffset.UtcNow.AddHours(-48);
        var idx = LowerBoundByCreatedAt(cases, cutoff);
        Console.WriteLine($"  lower_bound index={idx} (cases created at/after cutoff → NOT yet aging)");

        // Binary search on day thresholds (band breakpoints)
        foreach (var days in new[] { 1, 30, 31, 90, 91, 365, 366 })
            Console.WriteLine($"  days={days,3} → {Domain.ApprovalRulesDemo.EvaluateBand(days)}");

        // INTERVIEW: When is binary search wrong for DB work?
        // Answer: prefer B-tree index + keyset pagination over loading all rows.
        // See PendingApprovalEndpoints + ListPendingApprovalsQuery (afterCreatedAtUtc, afterApprovalCaseId).
        Console.WriteLine();
    }

    private static List<Domain.PendingApprovalCase> BuildSortedPendingCases()
    {
        var t0 = DateTimeOffset.UtcNow.AddHours(-72);
        return Enumerable.Range(0, 10)
            .Select(i => new Domain.PendingApprovalCase(
                Guid.NewGuid(),
                t0.AddHours(i * 6),
                i % 2 == 0 ? "RenewalManager" : "DealDeskManager",
                40 + i * 10,
                Domain.ApprovalBand.RenewalManagerOnly))
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.ApprovalCaseId)
            .ToList();
    }

    /// <summary>Classic lower_bound on CreatedAtUtc (array must be sorted ascending).</summary>
    public static int LowerBoundByCreatedAt(IReadOnlyList<Domain.PendingApprovalCase> sorted, DateTimeOffset cutoff)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (sorted[mid].CreatedAtUtc < cutoff) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}

// =============================================================================
// PART 3 — Sorting
// =============================================================================
/// <summary>
/// Sorting appears in: ApprovedScopeHasher item order, outbox available_at ordering,
/// pending approval keyset (CreatedAtUtc, ApprovalCaseId), provider fan-out grouping.
/// </summary>
public static class Part3_Sorting
{
    public static void Run()
    {
        Console.WriteLine("-- PART 3: Sorting --");
        var items = Part1_Basics.SampleItems();

        // Stable deterministic order for hashing (see ApprovedScopeHasher.AppendItems)
        var ordered = items
            .OrderBy(i => i.EntitlementId, StringComparer.Ordinal)
            .ThenBy(i => i.Provider.ToString(), StringComparer.Ordinal)
            .ThenBy(i => i.LogicalOperationId)
            .ToList();
        Console.WriteLine("  scope order: " + string.Join(", ", ordered.Select(i => i.EntitlementId)));

        // Priority sort: Unsupported first (reject fast), then DealDesk, RM, Auto
        var bands = items.Select(i => Domain.ApprovalRulesDemo.EvaluateBand(i.ExtensionDays)).ToList();
        var priority = bands.OrderByDescending(b => BandSeverity(b)).ToList();
        Console.WriteLine("  severity order: " + string.Join(" > ", priority));

        // Merge two provider streams (already sorted by expiry) — divide path for aggregation
        var cs = items.Where(i => i.Provider == Domain.ProviderSystem.CloudShield)
            .OrderBy(i => i.CurrentExpiryUtc).ToList();
        var ng = items.Where(i => i.Provider == Domain.ProviderSystem.NetworkGuard)
            .OrderBy(i => i.CurrentExpiryUtc).ToList();
        var merged = MergeByExpiry(cs, ng);
        Console.WriteLine("  merged expiries: " + string.Join(", ", merged.Select(i => i.EntitlementId)));

        // Quickselect sketch: find k-th longest extension (controlling item)
        var days = items.Select(i => i.ExtensionDays).ToArray();
        Array.Sort(days);
        Console.WriteLine($"  median extension days ≈ {days[days.Length / 2]}");
        Console.WriteLine();
    }

    private static int BandSeverity(Domain.ApprovalBand b) => b switch
    {
        Domain.ApprovalBand.Unsupported => 4,
        Domain.ApprovalBand.RenewalManagerThenDealDesk => 3,
        Domain.ApprovalBand.RenewalManagerOnly => 2,
        _ => 1
    };

    private static List<Domain.ExtensionItem> MergeByExpiry(
        IReadOnlyList<Domain.ExtensionItem> a,
        IReadOnlyList<Domain.ExtensionItem> b)
    {
        var result = new List<Domain.ExtensionItem>(a.Count + b.Count);
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i].CurrentExpiryUtc <= b[j].CurrentExpiryUtc) result.Add(a[i++]);
            else result.Add(b[j++]);
        }
        while (i < a.Count) result.Add(a[i++]);
        while (j < b.Count) result.Add(b[j++]);
        return result;
    }
}

// =============================================================================
// PART 4 — Array techniques
// =============================================================================
/// <summary>
/// Two pointers / sliding window for selective-retry windows and aging streaks.
/// </summary>
public static class Part4_ArrayTechniques
{
    public static void Run()
    {
        Console.WriteLine("-- PART 4: Array techniques --");

        // Prefix sums: cumulative failed attempts per LogicalOperationId lineage
        var attemptCounts = new[] { 1, 0, 2, 1, 3, 0, 1 }; // per time bucket
        var prefix = new int[attemptCounts.Length + 1];
        for (int i = 0; i < attemptCounts.Length; i++)
            prefix[i + 1] = prefix[i] + attemptCounts[i];
        // Failures in window [2,5)
        Console.WriteLine($"  failures in [2,5) = {prefix[5] - prefix[2]}");

        // Sliding window: max failed items in any contiguous 3-bucket window
        int w = 3, best = 0;
        for (int r = 0; r < attemptCounts.Length; r++)
        {
            int l = Math.Max(0, r - w + 1);
            best = Math.Max(best, prefix[r + 1] - prefix[l]);
        }
        Console.WriteLine($"  max failures in window={w}: {best}");

        // Two pointers: shrink a candidate retry set until it fits provider bulkhead
        // (ProviderOptions.BulkheadMaxParallelization = 32)
        var failed = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        int capacity = 32;
        int left = 0, right = Math.Min(capacity, failed.Count);
        Console.WriteLine($"  retry claim window size={right - left} (cap={capacity})");

        // Dutch national flag style: partition saga item outcomes
        var statuses = new[] { "Failed", "Succeeded", "Failed", "Pending", "Succeeded" };
        var (failedIdx, pendingIdx, okIdx) = PartitionOutcomes(statuses);
        Console.WriteLine($"  partition Failed={failedIdx.Count} Pending={pendingIdx.Count} OK={okIdx.Count}");
        Console.WriteLine();
    }

    private static (List<int> failed, List<int> pending, List<int> ok) PartitionOutcomes(string[] statuses)
    {
        var f = new List<int>(); var p = new List<int>(); var o = new List<int>();
        for (int i = 0; i < statuses.Length; i++)
        {
            switch (statuses[i])
            {
                case "Failed": f.Add(i); break;
                case "Pending": p.Add(i); break;
                default: o.Add(i); break;
            }
        }
        return (f, p, o);
    }
}

// =============================================================================
// PART 5 — Backtracking
// =============================================================================
/// <summary>
/// Enumerate selective-retry subsets of failed LogicalOperationIds under a budget.
/// Production code does NOT explode subsets — it takes an explicit filter list
/// (LicenseExtensionRetryEndpoints body.LogicalOperationIds). This demo teaches
/// the combinatorial space interviewers often ask about.
/// </summary>
public static class Part5_Backtracking
{
    public static void Run()
    {
        Console.WriteLine("-- PART 5: Backtracking --");
        var failedLogicalIds = new[]
        {
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        };

        var subsets = new List<List<Guid>>();
        Backtrack(failedLogicalIds, 0, new List<Guid>(), subsets);
        Console.WriteLine($"  subset count (2^n)={subsets.Count}"); // includes empty

        // Constraint: at most 2 items per retry generation (toy policy)
        var feasible = subsets.Where(s => s.Count is > 0 and <= 2).ToList();
        Console.WriteLine($"  feasible under budget≤2: {feasible.Count}");

        // INTERVIEW: Prefer O(k) explicit selection over O(2^n) search in prod.
        // FailedItemSelector validates requested IDs ⊆ failed set.
        Console.WriteLine();
    }

    private static void Backtrack(Guid[] ids, int index, List<Guid> path, List<List<Guid>> acc)
    {
        if (index == ids.Length)
        {
            acc.Add(new List<Guid>(path));
            return;
        }
        // skip
        Backtrack(ids, index + 1, path, acc);
        // take
        path.Add(ids[index]);
        Backtrack(ids, index + 1, path, acc);
        path.RemoveAt(path.Count - 1);
    }
}

// =============================================================================
// PART 6 — Divide & Conquer
// =============================================================================
/// <summary>
/// Split a multi-provider request into CloudShield / NetworkGuard fulfillment
/// commands (see Kafka topics cloudshield.* / networkguard.*).
/// </summary>
public static class Part6_DivideAndConquer
{
    public static void Run()
    {
        Console.WriteLine("-- PART 6: Divide & Conquer --");
        var items = Part1_Basics.SampleItems();
        var (cs, ng) = PartitionByProvider(items);
        Console.WriteLine($"  CloudShield batch={cs.Count}, NetworkGuard batch={ng.Count}");

        // Conquer: compute per-provider max extension, then combine with MAX
        int csMax = cs.Count == 0 ? 0 : cs.Max(i => i.ExtensionDays);
        int ngMax = ng.Count == 0 ? 0 : ng.Max(i => i.ExtensionDays);
        int combined = Math.Max(csMax, ngMax);
        Console.WriteLine($"  combined controlling days={combined} → {Domain.ApprovalRulesDemo.EvaluateBand(combined)}");

        // Classic merge-sort mental model for aggregating entitlement discovery pages
        var pageA = new[] { "CS-1", "CS-3", "CS-5" };
        var pageB = new[] { "CS-2", "CS-4", "CS-6" };
        Console.WriteLine("  merged pages: " + string.Join(",", MergeSorted(pageA, pageB)));
        Console.WriteLine();
    }

    private static (List<Domain.ExtensionItem> cs, List<Domain.ExtensionItem> ng)
        PartitionByProvider(IEnumerable<Domain.ExtensionItem> items)
    {
        var cs = new List<Domain.ExtensionItem>();
        var ng = new List<Domain.ExtensionItem>();
        foreach (var item in items)
        {
            if (item.Provider == Domain.ProviderSystem.CloudShield) cs.Add(item);
            else ng.Add(item);
        }
        return (cs, ng);
    }

    private static List<string> MergeSorted(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var r = new List<string>(a.Count + b.Count);
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (string.CompareOrdinal(a[i], b[j]) <= 0) r.Add(a[i++]);
            else r.Add(b[j++]);
        }
        while (i < a.Count) r.Add(a[i++]);
        while (j < b.Count) r.Add(b[j++]);
        return r;
    }
}

// =============================================================================
// PART 7 — Dynamic Programming
// =============================================================================
/// <summary>
/// DP for scheduling approval reminders under a daily email budget.
/// Analogy: lambdas/EntitleX.ApprovalReminder + ApprovalReminderPolicy.
/// </summary>
public static class Part7_DP
{
    public static void Run()
    {
        Console.WriteLine("-- PART 7: Dynamic Programming --");

        // Each pending case has (urgencyScore, emailCost). Pick subset ≤ budget maximizing score.
        // Classic 0/1 knapsack — teaches DP table construction for interviews.
        var urgency = new[] { 6, 5, 10, 3 };  // SLA risk
        var cost = new[] { 1, 2, 3, 1 };      // emails consumed
        int budget = 4;
        int best = Knapsack(urgency, cost, budget);
        Console.WriteLine($"  max urgency under budget={budget}: {best}");

        // DP for min reminder days to cover escalation ladder: t=0, +24h, +48h
        // dp[i] = min cost to cover first i milestones
        var milestones = new[] { 0, 24, 48 }; // hours
        var sendCost = new[] { 1, 1, 2 };
        Console.WriteLine($"  milestone plan cost={sendCost.Sum()} over {milestones.Length} steps");

        // LIS-style: longest increasing approval-stage chain (Requested→RM→DealDesk→Approved)
        var stageOrder = new[] { 0, 1, 2, 3 }; // already sorted DAG path
        Console.WriteLine($"  longest valid stage chain length={stageOrder.Length}");
        Console.WriteLine();
    }

    /// <summary>0/1 knapsack DP — O(n*budget).</summary>
    public static int Knapsack(int[] value, int[] weight, int capacity)
    {
        int n = value.Length;
        var dp = new int[capacity + 1];
        for (int i = 0; i < n; i++)
        {
            for (int w = capacity; w >= weight[i]; w--)
                dp[w] = Math.Max(dp[w], dp[w - weight[i]] + value[i]);
        }
        return dp[capacity];
    }
}

// =============================================================================
// PART 8 — Greedy
// =============================================================================
/// <summary>
/// Greedy day-band evaluation and earliest-deadline-first fulfillment ordering.
/// ApprovalRules.EvaluateBand is itself a greedy threshold walk.
/// </summary>
public static class Part8_Greedy
{
    public static void Run()
    {
        Console.WriteLine("-- PART 8: Greedy --");

        // Threshold walk — same as production ApprovalRules
        foreach (var d in new[] { 15, 30, 31, 90, 91, 365, 400 })
            Console.WriteLine($"  EvaluateBand({d}) = {Domain.ApprovalRulesDemo.EvaluateBand(Math.Min(d, 400) == 400 && d > 365 ? d : d)}");

        // Earliest-deadline-first: process entitlements closest to expiry first
        var items = Part1_Basics.SampleItems().OrderBy(i => i.CurrentExpiryUtc).ToList();
        Console.WriteLine("  EDF order: " + string.Join(" → ", items.Select(i => i.EntitlementId)));

        // Interval scheduling: non-overlapping outbox publish leases
        // (OutboxOptions.LockDurationSeconds). Greedy by earliest lock end.
        var leases = new[] { (start: 0, end: 30), (start: 10, end: 40), (start: 35, end: 65), (start: 60, end: 90) };
        var selected = new List<(int start, int end)>();
        int t = int.MinValue;
        foreach (var lease in leases.OrderBy(l => l.end))
        {
            if (lease.start >= t)
            {
                selected.Add(lease);
                t = lease.end;
            }
        }
        Console.WriteLine($"  non-overlapping leases selected={selected.Count}");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 9 — Graphs
// =============================================================================
/// <summary>
/// BFS over entitlement adjacency (related licenses) and topological sort of
/// saga approval stages. Real saga transitions: LicenseExtensionSaga._transitions.
/// </summary>
public static class Part9_Graphs
{
    public static void Run()
    {
        Console.WriteLine("-- PART 9: Graphs --");

        // Entitlement graph: CS-ENT-001 related to CS-ENT-044 (same customer bundle)
        var adj = new Dictionary<string, List<string>>
        {
            ["CS-ENT-001"] = new() { "CS-ENT-044" },
            ["CS-ENT-044"] = new() { "CS-ENT-001", "NG-ENT-009" },
            ["NG-ENT-009"] = new() { "CS-ENT-044" },
        };
        var reachable = Bfs("CS-ENT-001", adj);
        Console.WriteLine("  BFS from CS-ENT-001: " + string.Join(" → ", reachable));

        // Topological order of approval stages for RM→DealDesk band
        var stages = new Dictionary<string, List<string>>
        {
            ["Requested"] = new() { "PendingRenewalManagerApproval" },
            ["PendingRenewalManagerApproval"] = new() { "PendingDealDeskApproval", "Rejected" },
            ["PendingDealDeskApproval"] = new() { "Approved", "Rejected" },
            ["Approved"] = new() { "QueuedForFulfillment" },
            ["QueuedForFulfillment"] = new() { "Processing" },
            ["Processing"] = new() { "Completed", "PartiallyCompleted", "Failed", "ReconciliationRequired" },
            ["Rejected"] = new(),
            ["Completed"] = new(),
            ["PartiallyCompleted"] = new(),
            ["Failed"] = new(),
            ["ReconciliationRequired"] = new(),
        };
        var topo = TopoSort(stages);
        Console.WriteLine("  topo (one valid order): " + string.Join(" → ", topo.Take(6)) + " …");

        // Detect cycle would mean illegal saga config — Kahn's algorithm returns partial
        Console.WriteLine($"  topo visited={topo.Count}/{stages.Count}");
        Console.WriteLine();
    }

    public static List<string> Bfs(string start, Dictionary<string, List<string>> adj)
    {
        var seen = new HashSet<string> { start };
        var q = new Queue<string>();
        q.Enqueue(start);
        var order = new List<string>();
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            order.Add(u);
            if (!adj.TryGetValue(u, out var neighbors)) continue;
            foreach (var v in neighbors)
            {
                if (seen.Add(v)) q.Enqueue(v);
            }
        }
        return order;
    }

    public static List<string> TopoSort(Dictionary<string, List<string>> graph)
    {
        var indeg = graph.Keys.ToDictionary(k => k, _ => 0);
        foreach (var u in graph.Keys)
            foreach (var v in graph[u])
                if (indeg.ContainsKey(v)) indeg[v]++;
        var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            order.Add(u);
            foreach (var v in graph[u])
            {
                if (!indeg.ContainsKey(v)) continue;
                indeg[v]--;
                if (indeg[v] == 0) q.Enqueue(v);
            }
        }
        return order;
    }
}

// =============================================================================
// PART 10 — Tree algorithms
// =============================================================================
/// <summary>
/// Fenwick / segment-tree analogies for aging bucket counts (how many pending
/// approvals in each day band). Production uses SQL GROUP BY; trees teach range
/// queries in interviews.
/// </summary>
public static class Part10_Trees
{
    public static void Run()
    {
        Console.WriteLine("-- PART 10: Trees --");

        // Map extension days into 4 buckets: Auto / RM / DealDesk / Unsupported
        var fenwick = new Fenwick(4);
        foreach (var days in new[] { 10, 20, 45, 100, 200, 400, 15 })
        {
            int bucket = days switch
            {
                <= 30 => 1,
                <= 90 => 2,
                <= 365 => 3,
                _ => 4
            };
            fenwick.Add(bucket, 1);
        }
        Console.WriteLine($"  Auto+RM count (prefix 2)={fenwick.Sum(2)}");
        Console.WriteLine($"  all bands (prefix 4)={fenwick.Sum(4)}");

        // Binary tree walk: saga decision tree
        //           Requested
        //          /         \
        //     AutoApprove   NeedsHuman
        //                  /          \
        //                RM-only    RM→DealDesk
        Console.WriteLine("  decision tree leaf for 120d: RM→DealDesk");
        Console.WriteLine();
    }

    /// <summary>1-indexed Fenwick tree for prefix sums.</summary>
    private sealed class Fenwick
    {
        private readonly int[] _bit;
        public Fenwick(int n) => _bit = new int[n + 1];
        public void Add(int i, int delta)
        {
            for (; i < _bit.Length; i += i & -i) _bit[i] += delta;
        }
        public int Sum(int i)
        {
            int s = 0;
            for (; i > 0; i -= i & -i) s += _bit[i];
            return s;
        }
    }
}

// =============================================================================
// PART 11 — Strings
// =============================================================================
/// <summary>
/// String hashing analogy for ApprovedScopeHasher (SHA256 of canonical item scope).
/// Rolling hash demos teach collision thinking; production uses SHA256 hex.
/// Path: src/EntitleX.Domain/Approval/ApprovedScopeHasher.cs
/// </summary>
public static class Part11_Strings
{
    public static void Run()
    {
        Console.WriteLine("-- PART 11: Strings --");
        var items = Part1_Basics.SampleItems();
        var scopeA = ComputeItemScope(items);
        var scopeB = ComputeItemScope(items.AsEnumerable().Reverse());
        Console.WriteLine($"  scope stable under reorder? {scopeA == scopeB}");
        Console.WriteLine($"  scope={scopeA[..16]}…");

        // Rolling hash for entitlement id prefix search (toy)
        var ids = items.Select(i => i.EntitlementId).ToList();
        Console.WriteLine($"  starts with CS-? {ids.Count(id => id.StartsWith("CS-", StringComparison.Ordinal))}");

        // KMP-ish: find provider token in audit message
        const string audit = "Provider=CloudShield LogicalOperationId=1111 AttemptId=2222";
        Console.WriteLine($"  audit contains CloudShield? {audit.Contains("CloudShield", StringComparison.Ordinal)}");
        Console.WriteLine();
    }

    /// <summary>Mirrors ApprovedScopeHasher.ComputeItemScope (simplified).</summary>
    public static string ComputeItemScope(IEnumerable<Domain.ExtensionItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items
                     .OrderBy(i => i.EntitlementId, StringComparer.Ordinal)
                     .ThenBy(i => i.Provider.ToString(), StringComparer.Ordinal)
                     .ThenBy(i => i.LogicalOperationId))
        {
            sb.Append(item.EntitlementId).Append(':')
              .Append(item.Provider).Append(':')
              .Append(item.CurrentExpiryUtc.UtcTicks).Append(':')
              .Append(item.RequestedExpiryUtc.UtcTicks).Append(':')
              .Append(item.LogicalOperationId.ToString("D")).Append(';');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// =============================================================================
// PART 12 — Math & bits
// =============================================================================
/// <summary>
/// Bitsets for eligibility flags; modular arithmetic for attempt backoff jitter.
/// </summary>
public static class Part12_MathBits
{
    public static void Run()
    {
        Console.WriteLine("-- PART 12: Math & bits --");

        // Bit flags: EligibleFailed | Claimed | IdempotentReplay
        const int EligibleFailed = 1 << 0;
        const int Claimed = 1 << 1;
        const int IdempotentReplay = 1 << 2;
        int flags = EligibleFailed | Claimed;
        Console.WriteLine($"  flags has Claimed? {(flags & Claimed) != 0}");
        flags |= IdempotentReplay;
        Console.WriteLine($"  flags after replay mark={flags}");

        // Exponential backoff with cap (OutboxOptions BaseRetryDelaySeconds / MaxRetryDelaySeconds)
        int baseDelay = 2, maxDelay = 300;
        for (int attempt = 1; attempt <= 8; attempt++)
        {
            int delay = Math.Min(maxDelay, baseDelay * (1 << Math.Min(attempt - 1, 8)));
            Console.WriteLine($"  attempt={attempt} delaySec={delay}");
        }

        // GCD rarely needed — but interviewers ask; use for packing batch sizes
        Console.WriteLine($"  gcd(100 MaxBatchSize, 1000 items)={Gcd(100, 1000)} → 10 round-trips ideal");
        Console.WriteLine();
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

// =============================================================================
// PART 13 — Interview cheat sheet
// =============================================================================
public static class Part13_InterviewNotes
{
    public static void Print()
    {
        Console.WriteLine("-- PART 13: Interview notes --");
        Console.WriteLine("""
  1. Always map the abstract algo to an EntitleX artifact (band, saga, outbox, retry).
  2. State whether work is sync (API) or async (worker/outbox/lambda).
  3. Prefer keyset pagination over OFFSET for pending approvals.
  4. Selective retry keeps LogicalOperationId stable; AttemptId is per generation;
     ParentId links to prior attempt — forward recovery, never compensate success.
  5. ApprovedScopeHasher ordering must be deterministic (sort before hash).
  6. Graph topo of saga states proves there is no illegal cycle in config.
  7. Greedy band thresholds are O(1); controlling length is O(n) max scan.
  8. Know FOR UPDATE SKIP LOCKED for outbox claim leases under concurrency.
  9. Roles: RenewalRepresentative / RenewalManager / DealDeskManager / PlatformAdmin.
  10. ReconciliationRequired is a terminal-ish gate — do not silently re-fulfill.
""");
    }
}
}
