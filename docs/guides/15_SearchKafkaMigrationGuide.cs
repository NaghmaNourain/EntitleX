// =====================================================================================
//  15_SearchKafkaMigrationGuide.cs
//  EntitleX: entitlement search aggregation + messaging topology migration
//  (outbox → SQS fulfillment → Kafka provider commands/results).
//
//  HOW TO RUN (needs .NET 8 SDK, NO NuGet packages):
//      dotnet new console -n SearchKafkaMigrationGuide && cd SearchKafkaMigrationGuide
//      replace Program.cs with this file, then:  dotnet run
//
//  HOW TO READ
//    CONCEPT / WHY / TRADE-OFF / DEMO per section. Demos are in-memory simulations.
//    Real paths: src/EntitleX.Application/EntitlementAggregation/,
//                src/EntitleX.Messaging/, docs/event-contracts.md
//
//  TABLE OF CONTENTS
//   PART A  Entitlement search aggregation (fan-out)
//     A0  Mental model — control plane aggregates, providers own truth
//     A1  Fan-out to CloudShield + NetworkGuard
//     A2  Partial success semantics
//     A3  No platform DB transaction across remote HTTP
//     A4  Correlation ids on the search path
//   PART B  Messaging topology migration narrative
//     B0  Why not dual-write from the API
//     B1  Outbox → SQS fulfillment (work queue + DLQ)
//     B2  SQS → Kafka provider commands (fan-out)
//     B3  Kafka results → saga settlement
//     B4  Why this hybrid (SQS then Kafka) instead of "Kafka everywhere"
//   PART C  Contracts, versioning, consumers
//     C1  MessageEnvelope schema versioning
//     C2  Payload DTOs V1 (Fulfillment / Provider commands / results)
//     C3  Consumer groups
//     C4  Partition key = entitlement id (ordering)
//     C5  Idempotent consumers (processed_messages)
//   PART D  Backfill / migration patterns for message contracts
//     D1  Additive fields + schemaVersion discipline
//     D2  Dual-publish pitfalls (and why outbox avoids the worst)
//     D3  Replaying DLQ / poisoned outbox rows
//   PART E  Interview Q&A + end-to-end demo
// =====================================================================================

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EntitleX.Guides.SearchKafkaMigration;

public static class Program
{
    public static async Task Main()
    {
        await PartA_AggregationAsync();
        await PartB_TopologyAsync();
        await PartC_ContractsAsync();
        await PartD_MigrationPatternsAsync();
        await PartE_InterviewAndE2EAsync();

        Console.WriteLine();
        Console.WriteLine("All Search/Kafka migration demos finished.");
    }

    // =================================================================================
    // PART A  ENTITLEMENT SEARCH AGGREGATION
    // =================================================================================
    //
    // REAL: EntitlementAggregationService in
    //   src/EntitleX.Application/EntitlementAggregation/EntitlementAggregationService.cs
    // Endpoint: EntitlementAggregationEndpoints (correlation from X-Correlation-ID middleware).
    //
    // CONCEPT
    //   The control plane does NOT store the live entitlement catalog as source of truth.
    //   Search fans out HTTP discovery to CloudShield + NetworkGuard, merges normalized
    //   DTOs, and returns IsPartial=true if any provider failed.
    //
    // WHY
    //   Vendor systems own entitlement state. Aggregation gives Renewal Reps a single UI
    //   without inventing a second catalog that drifts.
    //
    // TRADE-OFF
    //   Partial results: UX must show which provider failed. Latency = slowest provider
    //   (Task.WhenAll). No distributed transaction — by design.
    // =================================================================================
    private static async Task PartA_AggregationAsync()
    {
        Header("PART A  ENTITLEMENT SEARCH AGGREGATION");
        A0_MentalModel();
        await A1_FanOutAsync();
        await A2_PartialSuccessAsync();
        A3_NoDbAcrossHttp();
        A4_CorrelationOnSearch();
    }

    private static void A0_MentalModel()
    {
        Section("A0  Mental model — aggregate, don't own the catalog");

        Console.WriteLine("  UI / API  →  EntitlementAggregationService");
        Console.WriteLine("                 ├─ IProviderEntitlementClient CloudShield");
        Console.WriteLine("                 └─ IProviderEntitlementClient NetworkGuard");
        Console.WriteLine();
        Console.WriteLine("  Returns CustomerEntitlementAggregationDto:");
        Console.WriteLine("    CustomerId, CorrelationId, Entitlements[], ProviderContributions[], IsPartial");
        Console.WriteLine("  Cite: EntitlementAggregationService.cs, Models/, SearchCustomerEntitlements/");
    }

    private static async Task A1_FanOutAsync()
    {
        Section("A1  Fan-out demo — both providers healthy");

        var svc = new ToyAggregationService(new IToyProvider[]
        {
            new ToyProvider("CloudShield", delayMs: 30, fail: false),
            new ToyProvider("NetworkGuard", delayMs: 45, fail: false),
        });

        var result = await svc.AggregateAsync("cust-100", "corr-aaa");
        PrintAggregation(result);
    }

    private static async Task A2_PartialSuccessAsync()
    {
        Section("A2  Partial success — NetworkGuard down, CloudShield OK");

        var svc = new ToyAggregationService(new IToyProvider[]
        {
            new ToyProvider("CloudShield", delayMs: 20, fail: false),
            new ToyProvider("NetworkGuard", delayMs: 20, fail: true),
        });

        var result = await svc.AggregateAsync("cust-100", "corr-bbb");
        PrintAggregation(result);

        Console.WriteLine("  WHY return 200 + IsPartial instead of 502?");
        Console.WriteLine("    Operators can still extend CloudShield entitlements; failing closed");
        Console.WriteLine("    on one vendor would block all renewals. UI surfaces ProviderContributions.");
    }

    private static void A3_NoDbAcrossHttp()
    {
        Section("A3  No platform DB transaction across remote HTTP");

        Console.WriteLine("  WRONG: BEGIN; SELECT ...; HTTP CloudShield; HTTP NetworkGuard; COMMIT;");
        Console.WriteLine("    → locks held during network; timeouts; connection pool exhaustion.");
        Console.WriteLine("  RIGHT (EntitleX): fan-out HTTP only; optional later persist of search audit");
        Console.WriteLine("    is a separate concern. Comment on the real service says exactly this.");
    }

    private static void A4_CorrelationOnSearch()
    {
        Section("A4  Correlation id on the search path");

        Console.WriteLine("  CorrelationIdMiddleware sets X-Correlation-ID → Items + LogContext.");
        Console.WriteLine("  Aggregation logs include CorrelationId for each provider call.");
        Console.WriteLine("  Same id flows into approval/fulfillment envelopes later (MessageEnvelope).");
        Console.WriteLine("  Cite: src/EntitleX.Api/Middleware/CorrelationIdMiddleware.cs");
    }

    // =================================================================================
    // PART B  MESSAGING TOPOLOGY MIGRATION NARRATIVE
    // =================================================================================
    //
    // Historical teaching arc (what interviewers want):
    //   1) Naive: API dual-writes DB + queue
    //   2) Outbox in same UoW
    //   3) SQS as durable work queue + DLQ for control-plane dispatch
    //   4) Kafka for per-provider command fan-out + shared results (ordering by key)
    //
    // REAL flow (docs/event-contracts.md):
    //   approval final → outbox_messages → OutboxPublisher → SQS
    //   → FulfillmentWorker → Kafka commands (key=entitlement id)
    //   → simulator consumers → results topic → ProviderResultProcessor → saga
    // =================================================================================
    private static async Task PartB_TopologyAsync()
    {
        Header("PART B  MESSAGING TOPOLOGY MIGRATION NARRATIVE");
        B0_WhyNotDualWrite();
        await B1_OutboxToSqsAsync();
        await B2_SqsToKafkaAsync();
        await B3_ResultsToSagaAsync();
        B4_WhyHybrid();
    }

    private static void B0_WhyNotDualWrite()
    {
        Section("B0  Why not dual-write from the API");

        Console.WriteLine("  Dual-write failure modes:");
        Console.WriteLine("    A) DB commit OK, SQS send fails  → request approved but never fulfilled");
        Console.WriteLine("    B) SQS send OK, DB rolls back    → phantom work, confusing operators");
        Console.WriteLine("  Outbox: INSERT outbox_messages in SAME Postgres transaction as saga/request.");
        Console.WriteLine("  Publisher is a separate process with SKIP LOCKED + retries → Poison.");
        Console.WriteLine("  Cite: interview-guide.md §3, OutboxPublisher worker");
    }

    private static async Task B1_OutboxToSqsAsync()
    {
        Section("B1  Outbox → SQS fulfillment (work queue + DLQ)");

        var topology = new ToyTopology();
        var requestId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var messageId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await topology.ApproveAndEnqueueOutboxAsync(requestId, messageId);
        await topology.RelayOutboxToSqsAsync();

        Console.WriteLine($"  SQS depth={topology.SqsDepth} DLQ depth={topology.DlqDepth}");
        Console.WriteLine("  Queue names: entitlex-fulfillment / entitlex-fulfillment-dlq");
        Console.WriteLine("  Config: MessagingOptions.Sqs (VisibilityTimeout 30, WaitTime 10, MaxMessages 5)");
        Console.WriteLine("  AWS: modules/sqs maxReceiveCount=5; Local: init-sqs.sh");
    }

    private static async Task B2_SqsToKafkaAsync()
    {
        Section("B2  SQS → Kafka provider commands");

        var topology = new ToyTopology();
        var requestId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await topology.ApproveAndEnqueueOutboxAsync(requestId, messageId, items: new[]
        {
            new ToyItem("ent-cs-1", "CloudShield"),
            new ToyItem("ent-ng-1", "NetworkGuard"),
            new ToyItem("ent-cs-2", "CloudShield"),
        });
        await topology.RelayOutboxToSqsAsync();
        await topology.ConsumeSqsAndPublishKafkaAsync();

        Console.WriteLine("  Kafka topics after fan-out:");
        foreach (var (topic, count) in topology.KafkaTopicCounts())
            Console.WriteLine($"    {topic}: {count} message(s)");

        Console.WriteLine("  Partition keys used:");
        foreach (var k in topology.LastPartitionKeys)
            Console.WriteLine($"    key={k}");
    }

    private static async Task B3_ResultsToSagaAsync()
    {
        Section("B3  Kafka results → saga settlement");

        var topology = new ToyTopology();
        await topology.SeedResultAsync("ent-cs-1", "CloudShield", ok: true);
        await topology.SeedResultAsync("ent-ng-1", "NetworkGuard", ok: false);
        var saga = await topology.ConsumeResultsAsync();

        Console.WriteLine($"  Saga items: success={saga.Success} failed={saga.Failed} → {saga.State}");
        Console.WriteLine("  REAL: ProviderLicenseExtensionCompletedV1 / FailedV1 on entitlex.provider.results");
        Console.WriteLine("  Consumer group: entitlex-orchestrator-results (MessagingOptions.Kafka)");
    }

    private static void B4_WhyHybrid()
    {
        Section("B4  Why SQS then Kafka (not Kafka everywhere)");

        Console.WriteLine("  SQS strengths for control-plane dispatch:");
        Console.WriteLine("    • Simple competing consumers for FulfillmentWorker");
        Console.WriteLine("    • Native DLQ + maxReceiveCount (ops-friendly poison handling)");
        Console.WriteLine("    • LocalStack parity with AWS module");
        Console.WriteLine("  Kafka strengths for provider edge:");
        Console.WriteLine("    • Per-entitlement ordering via partition key");
        Console.WriteLine("    • Independent consumer groups per simulator");
        Console.WriteLine("    • Shared results topic for orchestrator");
        Console.WriteLine("  TRADE-OFF: two transports to reason about — documented in event-contracts.md");
    }

    // =================================================================================
    // PART C  CONTRACTS, VERSIONING, CONSUMERS
    // =================================================================================
    private static async Task PartC_ContractsAsync()
    {
        Header("PART C  CONTRACTS, VERSIONING, CONSUMERS");
        C1_EnvelopeVersioning();
        C2_PayloadV1();
        C3_ConsumerGroups();
        C4_PartitionKeyOrdering();
        await C5_IdempotentConsumersAsync();
    }

    private static void C1_EnvelopeVersioning()
    {
        Section("C1  MessageEnvelope schema versioning");

        // Mirrors src/EntitleX.Messaging/Contracts/MessageEnvelope.cs shape.
        var envelope = ToyEnvelope.Create(
            schemaVersion: ToyEnvelope.CurrentSchemaVersion,
            messageType: ToyMessageTypes.FulfillmentCommand,
            messageId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            payload: new { requestId = Guid.NewGuid(), extensionNumber = "EXT-1" });

        Console.WriteLine($"  schemaVersion={envelope.SchemaVersion} type={envelope.MessageType}");
        Console.WriteLine($"  messageId={envelope.MessageId}");
        Console.WriteLine("  Fields: SchemaVersion, MessageType, MessageId, CorrelationId, CausationId,");
        Console.WriteLine("          RequestId, LogicalOperationId, OccurredAtUtc, Payload");
        Console.WriteLine("  CurrentSchemaVersion = \"1\"; payload DTOs suffixed V1.");
        Console.WriteLine("  Cite: MessageEnvelope.cs, docs/event-contracts.md");
    }

    private static void C2_PayloadV1()
    {
        Section("C2  Payload DTOs V1");

        Console.WriteLine("  FulfillmentCommandV1(RequestId, ExtensionNumber, Items[])");
        Console.WriteLine("    FulfillmentItemV1(RequestItemId, EntitlementId, Provider,");
        Console.WriteLine("      LogicalOperationId, AttemptId, CurrentExpiryUtc, RequestedExpiryUtc)");
        Console.WriteLine("  ProviderLicenseExtensionCommandV1(... NewExpiryUtc)");
        Console.WriteLine("  ProviderLicenseExtensionCompletedV1(... Previous/NewExpiry, AlreadyApplied)");
        Console.WriteLine("  ProviderLicenseExtensionFailedV1(... ErrorCode, ErrorMessage)");
        Console.WriteLine();
        Console.WriteLine("  MessageTypes constants:");
        Console.WriteLine($"    {ToyMessageTypes.FulfillmentCommand}");
        Console.WriteLine($"    {ToyMessageTypes.ProviderCommand}");
        Console.WriteLine($"    {ToyMessageTypes.ProviderCompleted}");
        Console.WriteLine($"    {ToyMessageTypes.ProviderFailed}");
    }

    private static void C3_ConsumerGroups()
    {
        Section("C3  Consumer groups");

        var groups = new[]
        {
            ("entitlex-fulfillment", "SQS conceptual / Kafka options name for fulfillment side"),
            ("cloudshield-commands", "CloudShield simulator command consumers"),
            ("networkguard-commands", "NetworkGuard simulator command consumers"),
            ("entitlex-orchestrator-results", "Platform result processor"),
        };

        foreach (var (name, role) in groups)
            Console.WriteLine($"  {name,-32} → {role}");

        Console.WriteLine("  WHY separate groups? Simulators scale independently; orchestrator");
        Console.WriteLine("  must not compete with vendor consumers for command partitions.");
    }

    private static void C4_PartitionKeyOrdering()
    {
        Section("C4  Partition key = entitlement id");

        var demo = new OrderingDemo(partitionCount: 3);
        var events = new[]
        {
            ("ent-A", "cmd-1"), ("ent-B", "cmd-1"), ("ent-A", "cmd-2"),
            ("ent-A", "cmd-3"), ("ent-B", "cmd-2"),
        };
        foreach (var (ent, cmd) in events)
            demo.Publish(ent, cmd);

        foreach (var p in demo.Partitions)
            Console.WriteLine($"  partition {p.Key}: [{string.Join(", ", p.Value)}]");

        Console.WriteLine("  Guaranteed: all ent-A commands stay ordered on one partition.");
        Console.WriteLine("  NOT guaranteed: ordering across different entitlements.");
        Console.WriteLine("  REAL: KafkaMessagePublisher Key = partitionKey ?? messageId");
        Console.WriteLine("  Fulfillment sets partitionKey to entitlement id per command.");
    }

    private static async Task C5_IdempotentConsumersAsync()
    {
        Section("C5  Idempotent consumers — processed_messages");

        var store = new ToyIdempotencyStore();
        var consumer = "entitlex-provider-results";
        var messageId = "11111111-2222-3333-4444-555555555555";

        var first = await store.TryBeginAsync(consumer, messageId);
        var second = await store.TryBeginAsync(consumer, messageId);

        Console.WriteLine($"  first delivery TryBegin={first}  second delivery TryBegin={second}");
        Console.WriteLine("  REAL: IConsumerIdempotencyStore.TryBeginAsync(consumerName, idempotencyKey)");
        Console.WriteLine("  At-least-once SQS/Kafka + DB unique (consumerName, idempotencyKey).");
        Console.WriteLine("  Stack with provider unique logical_operation_id — see idempotency.md");
    }

    // =================================================================================
    // PART D  BACKFILL / MIGRATION PATTERNS
    // =================================================================================
    private static async Task PartD_MigrationPatternsAsync()
    {
        Header("PART D  BACKFILL / MIGRATION PATTERNS FOR MESSAGE CONTRACTS");
        D1_AdditiveVersioning();
        await D2_DualPublishPitfallsAsync();
        await D3_ReplayAsync();
    }

    private static void D1_AdditiveVersioning()
    {
        Section("D1  Additive fields + schemaVersion discipline");

        Console.WriteLine("  Rules EntitleX follows (stubs at schema \"1\"):");
        Console.WriteLine("    1. Never reuse messageType for a breaking payload shape.");
        Console.WriteLine("    2. Prefer FulfillmentCommandV2 + schemaVersion \"2\" for breaks.");
        Console.WriteLine("    3. Consumers must ignore unknown JSON fields (forward compatible).");
        Console.WriteLine("    4. Producers may only add optional fields within V1 if truly optional.");
        Console.WriteLine("    5. LogicalOperationId remains the stable item intent across retries.");

        var v1 = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["entitlementId"] = "ent-1",
            ["newExpiryUtc"] = "2027-01-01T00:00:00Z"
        });
        var v1Plus = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["entitlementId"] = "ent-1",
            ["newExpiryUtc"] = "2027-01-01T00:00:00Z",
            ["reasonCode"] = "customer-request" // additive
        });

        Console.WriteLine($"  V1 JSON:     {v1}");
        Console.WriteLine($"  V1+additive: {v1Plus}  (old consumers ignore reasonCode)");
    }

    private static async Task D2_DualPublishPitfallsAsync()
    {
        Section("D2  Dual-publish pitfalls avoided by outbox");

        Console.WriteLine("  Anti-pattern during migrations: API writes V1 to SQS AND V2 to Kafka.");
        Console.WriteLine("  Crash windows create split-brain consumers.");
        Console.WriteLine();
        Console.WriteLine("  EntitleX approach:");
        Console.WriteLine("    • Single outbox row is source of intent");
        Console.WriteLine("    • Publisher (or fulfillment stage) owns transport mapping");
        Console.WriteLine("    • Contract evolution happens at processor boundaries with version checks");

        var outbox = new ToyOutboxStore();
        await outbox.AddAsync(Guid.NewGuid(), ToyMessageTypes.FulfillmentCommand, schema: "1");
        // Migration: processor upgrades payload when publishing Kafka, still one outbox commit.
        var row = outbox.Pending.Single();
        Console.WriteLine($"  Outbox row schema={row.Schema} type={row.Type} — one write, many downstream shapes.");
    }

    private static async Task D3_ReplayAsync()
    {
        Section("D3  Replaying DLQ / poisoned outbox");

        var topology = new ToyTopology();
        var id = Guid.NewGuid();
        await topology.ApproveAndEnqueueOutboxAsync(Guid.NewGuid(), id);
        await topology.RelayOutboxToSqsAsync();
        await topology.ForcePoisonToDlqAsync();

        Console.WriteLine($"  After poison: SQS={topology.SqsDepth} DLQ={topology.DlqDepth}");
        await topology.RedriveDlqAsync();
        Console.WriteLine($"  After redrive: SQS={topology.SqsDepth} DLQ={topology.DlqDepth}");
        Console.WriteLine("  Outbox Poison rows: ops re-queue by resetting status after fix.");
        Console.WriteLine("  Idempotent consumers make safe redelivery possible.");
    }

    // =================================================================================
    // PART E  INTERVIEW + E2E DEMO
    // =================================================================================
    private static async Task PartE_InterviewAndE2EAsync()
    {
        Header("PART E  INTERVIEW Q&A + END-TO-END DEMO");

        Qa("How does entitlement search tolerate one vendor outage?",
            "Fan-out WhenAll; IsPartial=true; ProviderContributions carry errors; no DB txn across HTTP.");

        Qa("Why SQS between outbox and Kafka?",
            "Durable competing-consumer work queue + DLQ for control-plane dispatch; Kafka for ordered provider edge.");

        Qa("What orders provider commands?",
            "Kafka partition key = entitlement id; same entitlement → same partition → ordered.");

        Qa("How do you migrate message contracts?",
            "Additive optional fields within V1; breaking changes → V2 + schemaVersion; outbox remains single intent.");

        Qa("What makes consumers safe under at-least-once?",
            "processed_messages unique (consumer, key) + provider logical_operation_id uniqueness.");

        Section("E2E mini simulation");
        var topology = new ToyTopology();
        var requestId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await topology.ApproveAndEnqueueOutboxAsync(requestId, messageId, new[]
        {
            new ToyItem("ent-42", "CloudShield"),
            new ToyItem("ent-99", "NetworkGuard"),
        });
        await topology.RelayOutboxToSqsAsync();
        await topology.ConsumeSqsAndPublishKafkaAsync();
        await topology.SeedResultAsync("ent-42", "CloudShield", ok: true);
        await topology.SeedResultAsync("ent-99", "NetworkGuard", ok: true);
        var saga = await topology.ConsumeResultsAsync();
        Console.WriteLine($"  Final saga state={saga.State} success={saga.Success} failed={saga.Failed}");
        Console.WriteLine("  Paths: EntitlementAggregation + EntitleX.Messaging + event-contracts.md");
    }

    // ---------------------------------------------------------------------------------
    // Helpers + inline types
    // ---------------------------------------------------------------------------------
    private static void Header(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 78));
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {title}");
    }

    private static void Qa(string q, string a)
    {
        Console.WriteLine($"  Q: {q}");
        Console.WriteLine($"  A: {a}");
        Console.WriteLine();
    }

    private static void PrintAggregation(ToyAggregationResult result)
    {
        Console.WriteLine($"  customer={result.CustomerId} correlation={result.CorrelationId} partial={result.IsPartial}");
        foreach (var p in result.Providers)
            Console.WriteLine($"    provider={p.Provider} ok={p.Succeeded} count={p.Count} err={p.Error ?? "-"}");
        foreach (var e in result.Entitlements)
            Console.WriteLine($"    entitlement {e.Provider}/{e.EntitlementId} expiry={e.ExpiryUtc:yyyy-MM-dd}");
    }

    private interface IToyProvider
    {
        string Name { get; }
        Task<IReadOnlyList<ToyEntitlement>> DiscoverAsync(string customerId);
    }

    private sealed class ToyProvider : IToyProvider
    {
        private readonly int _delayMs;
        private readonly bool _fail;
        public string Name { get; }
        public ToyProvider(string name, int delayMs, bool fail)
        {
            Name = name; _delayMs = delayMs; _fail = fail;
        }

        public async Task<IReadOnlyList<ToyEntitlement>> DiscoverAsync(string customerId)
        {
            await Task.Delay(_delayMs);
            if (_fail) throw new InvalidOperationException($"{Name} unavailable");
            return new[]
            {
                new ToyEntitlement(Name, $"{Name.ToLowerInvariant()}-{customerId}-1", DateTimeOffset.UtcNow.AddDays(30)),
                new ToyEntitlement(Name, $"{Name.ToLowerInvariant()}-{customerId}-2", DateTimeOffset.UtcNow.AddDays(90)),
            };
        }
    }

    private sealed class ToyAggregationService
    {
        private readonly IReadOnlyList<IToyProvider> _providers;
        public ToyAggregationService(IReadOnlyList<IToyProvider> providers) => _providers = providers;

        public async Task<ToyAggregationResult> AggregateAsync(string customerId, string correlationId)
        {
            var tasks = _providers.Select(async p =>
            {
                try
                {
                    var items = await p.DiscoverAsync(customerId);
                    return (p.Name, true, items, (string?)null);
                }
                catch (Exception ex)
                {
                    return (p.Name, false, (IReadOnlyList<ToyEntitlement>)Array.Empty<ToyEntitlement>(), ex.Message);
                }
            });

            var parts = await Task.WhenAll(tasks);
            var entitlements = parts.Where(p => p.Item2).SelectMany(p => p.Item3)
                .OrderBy(e => e.Provider).ThenBy(e => e.EntitlementId).ToList();
            var providers = parts.Select(p => new ToyProviderContribution(p.Item1, p.Item2, p.Item3.Count, p.Item4)).ToList();
            return new ToyAggregationResult(customerId, correlationId, entitlements, providers, providers.Any(x => !x.Succeeded));
        }
    }

    private sealed record ToyEntitlement(string Provider, string EntitlementId, DateTimeOffset ExpiryUtc);
    private sealed record ToyProviderContribution(string Provider, bool Succeeded, int Count, string? Error);
    private sealed record ToyAggregationResult(
        string CustomerId,
        string CorrelationId,
        IReadOnlyList<ToyEntitlement> Entitlements,
        IReadOnlyList<ToyProviderContribution> Providers,
        bool IsPartial);

    private static class ToyMessageTypes
    {
        public const string FulfillmentCommand = "entitlex.fulfillment.command";
        public const string ProviderCommand = "entitlex.provider.license-extension.command";
        public const string ProviderCompleted = "entitlex.provider.license-extension.completed";
        public const string ProviderFailed = "entitlex.provider.license-extension.failed";
    }

    private sealed record ToyEnvelope(
        string SchemaVersion,
        string MessageType,
        Guid MessageId,
        Guid? CorrelationId,
        string PayloadJson)
    {
        public const string CurrentSchemaVersion = "1";

        public static ToyEnvelope Create(string schemaVersion, string messageType, Guid messageId, Guid? correlationId, object payload) =>
            new(schemaVersion, messageType, messageId, correlationId, JsonSerializer.Serialize(payload));
    }

    private sealed record ToyItem(string EntitlementId, string Provider);

    private sealed class ToyTopology
    {
        private readonly List<(Guid MessageId, string Body)> _outbox = new();
        private readonly Queue<string> _sqs = new();
        private readonly Queue<string> _dlq = new();
        private readonly Dictionary<string, List<string>> _kafka = new();
        private readonly List<(string EntitlementId, string Provider, bool Ok)> _results = new();
        public List<string> LastPartitionKeys { get; } = new();
        public int SqsDepth => _sqs.Count;
        public int DlqDepth => _dlq.Count;

        public Task ApproveAndEnqueueOutboxAsync(Guid requestId, Guid messageId, IReadOnlyList<ToyItem>? items = null)
        {
            items ??= new[] { new ToyItem("ent-default", "CloudShield") };
            var payload = new
            {
                schemaVersion = "1",
                messageType = ToyMessageTypes.FulfillmentCommand,
                messageId,
                requestId,
                items
            };
            _outbox.Add((messageId, JsonSerializer.Serialize(payload)));
            return Task.CompletedTask;
        }

        public Task RelayOutboxToSqsAsync()
        {
            foreach (var row in _outbox.ToList())
            {
                _sqs.Enqueue(row.Body);
                _outbox.Remove(row);
            }
            return Task.CompletedTask;
        }

        public Task ConsumeSqsAndPublishKafkaAsync()
        {
            while (_sqs.Count > 0)
            {
                var body = _sqs.Dequeue();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("items", out var items)) continue;
                foreach (var item in items.EnumerateArray())
                {
                    var ent = item.GetProperty("EntitlementId").GetString()!;
                    var provider = item.GetProperty("Provider").GetString()!;
                    var topic = provider switch
                    {
                        "CloudShield" => "cloudshield.license-extension.commands",
                        "NetworkGuard" => "networkguard.license-extension.commands",
                        _ => "unknown.commands"
                    };
                    if (!_kafka.ContainsKey(topic)) _kafka[topic] = new List<string>();
                    _kafka[topic].Add(ent);
                    LastPartitionKeys.Add(ent);
                }
            }
            return Task.CompletedTask;
        }

        public IEnumerable<(string Topic, int Count)> KafkaTopicCounts() =>
            _kafka.Select(kv => (kv.Key, kv.Value.Count));

        public Task SeedResultAsync(string entitlementId, string provider, bool ok)
        {
            _results.Add((entitlementId, provider, ok));
            return Task.CompletedTask;
        }

        public Task<(int Success, int Failed, string State)> ConsumeResultsAsync()
        {
            var success = _results.Count(r => r.Ok);
            var failed = _results.Count(r => !r.Ok);
            var state = (success, failed) switch
            {
                (_, 0) when success > 0 => "Completed",
                (0, _) when failed > 0 => "Failed",
                _ when success > 0 && failed > 0 => "PartiallyCompleted",
                _ => "Pending"
            };
            return Task.FromResult((success, failed, state));
        }

        public Task ForcePoisonToDlqAsync()
        {
            while (_sqs.Count > 0) _dlq.Enqueue(_sqs.Dequeue());
            return Task.CompletedTask;
        }

        public Task RedriveDlqAsync()
        {
            while (_dlq.Count > 0) _sqs.Enqueue(_dlq.Dequeue());
            return Task.CompletedTask;
        }
    }

    private sealed class OrderingDemo
    {
        private readonly int _partitions;
        public Dictionary<int, List<string>> Partitions { get; } = new();
        public OrderingDemo(int partitionCount)
        {
            _partitions = partitionCount;
            for (var i = 0; i < partitionCount; i++) Partitions[i] = new List<string>();
        }

        public void Publish(string entitlementId, string commandId)
        {
            var partition = Math.Abs(entitlementId.GetHashCode()) % _partitions;
            Partitions[partition].Add($"{entitlementId}:{commandId}");
        }
    }

    private sealed class ToyIdempotencyStore
    {
        private readonly ConcurrentDictionary<(string Consumer, string Key), byte> _seen = new();
        public Task<bool> TryBeginAsync(string consumer, string key) =>
            Task.FromResult(_seen.TryAdd((consumer, key), 0));
    }

    private sealed class ToyOutboxStore
    {
        public List<OutboxRow> Pending { get; } = new();
        public Task AddAsync(Guid id, string type, string schema)
        {
            Pending.Add(new OutboxRow(id, type, schema));
            return Task.CompletedTask;
        }
    }

    private sealed record OutboxRow(Guid Id, string Type, string Schema);
}
