// =============================================================================
// EntitleX Educational Guide 12 — Cloud Reliability
// File: docs/guides/12_CloudReliabilityGuide.cs
// =============================================================================
// Standalone compilable demos + runbook-style commentary for operating the
// EntitleX license-extension control plane reliably in Compose / ECS labs.
//
// Run (optional):
//   dotnet new console -n Guide12 -o /tmp/Guide12 --force
//   cp docs/guides/12_CloudReliabilityGuide.cs /tmp/Guide12/Program.cs
//   dotnet run --project /tmp/Guide12
//
// Real paths:
//   src/EntitleX.Api/Program.cs                      (/health, /health/ready, /health/providers)
//   src/EntitleX.Infrastructure/Providers/ProviderHealthCheck.cs
//   src/EntitleX.Application/EntitlementAggregation/EntitlementAggregationService.cs
//   src/EntitleX.Api/Middleware/CorrelationIdMiddleware.cs
//   src/EntitleX.Infrastructure/Outbox/*
//   docs/{observability,deployment,failure-scenarios,performance}.md
//   infra/terraform/ (optional ECS / SQS / Lambda)
// =============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// #############################################################################
// TABLE OF CONTENTS
// #############################################################################
//  1. Reliability goals & SLOs for fulfillment
//  2. Health checks — /health, /health/ready, /health/providers
//  3. Graceful degradation — EntitlementAggregationService partial success
//  4. Timeouts + retries + circuit breaker (ops view)
//  5. Token validation & JWT RBAC blast radius
//  6. Correlation IDs & W3C trace propagation
//  7. DLQ — SQS maxReceiveCount 5
//  8. Poison outbox — MaxAttempts → Poison
//  9. Observability — OTel→Jaeger, Prometheus :9180+
// 10. Canary / deploy notes for ECS
// 11. Runbooks (comment playbooks)
// 12. SLI burn / error budget intuition
// 13. Dependency criticality matrix
// 14. Demo runner
// #############################################################################

namespace EntitleX.Guides.CloudReliability;

internal static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX Guide 12: Cloud Reliability ===\n");
        Demo01_SloFraming.Run();
        await Demo02_HealthChecks.RunAsync();
        await Demo03_GracefulDegradation.RunAsync();
        await Demo04_TimeoutsRetriesCb.RunAsync();
        Demo05_TokenValidation.Run();
        Demo06_CorrelationAndTracing.Run();
        Demo07_SqsDlq.Run();
        Demo08_PoisonOutbox.Run();
        Demo09_ObservabilityStack.Run();
        Demo10_CanaryEcsNotes.Run();
        Demo11_Runbooks.Run();
        Demo12_SliBurnSimulation.Run();
        Demo13_DependencyMatrix.Run();
        Demo14_LoadSheddingAndQueues.Run();
        Demo15_SecurityReliability.Run();
        Demo16_SyntheticChecks.Run();
        Console.WriteLine("\nGuide 12 complete. Pair with docs/failure-scenarios.md on-call.");
    }
}

// =============================================================================
// 1. Reliability goals & SLOs for fulfillment
// =============================================================================
// Interview framing: SLOs are product promises; SLIs are measured signals;
// error budgets decide how hard to push canaries / feature work.
public static class Demo01_SloFraming
{
    public static void Run()
    {
        Console.WriteLine("-- Demo01 SLOs for fulfillment --");
        Console.WriteLine("""
          Example EntitleX fulfillment SLOs (portfolio targets — tune in prod):

          SLI                                 SLO (30-day)
          ---------------------------------   ---------------------------
          Final-approval → outbox Published   99.9% < 30s
          Outbox → SQS accept                 99.9% < 5s
          Provider item success (steady)      ≥ 99% (excl. injected faults)
          Selective-retry API availability    99.9% (5xx ratio)
          Reconciliation backlog age          99% of Uncertain < 15m
          Poison outbox rate                  < 0.1% of publishes

          Error budget example:
            99.9% monthly ≈ 43 minutes of burn. A bad canary that spikes 5xx
            for 10 minutes burns a large slice — roll back, don't "hope".

          Metrics (low cardinality only — docs/observability.md):
            entitlex_fulfillment_outcomes_total{outcome}
            entitlex_outbox_backlog
            entitlex_reconciliation_backlog
            entitlex_sqs_approximate_depth
            entitlex_provider_latency_seconds{provider,method}
            entitlex_circuit_breaker_transitions_total{provider,state}
            entitlex_saga_transitions_total{from_state,to_state}

          NEVER label metrics with requestId / messageId / logicalOperationId.
        """);
    }
}

// =============================================================================
// 2. Health checks
// =============================================================================
// Real mapping in EntitleX.Api/Program.cs:
//   GET /health           — liveness (process up)
//   GET /health/ready     — readiness (MapHealthChecks)
//   GET /health/providers — provider-tagged checks only
// ProviderHealthCheck probes CloudShield/NetworkGuard GET /health via clients.

public enum ProbeStatus { Healthy, Degraded, Unhealthy }

public sealed record HealthReport(string Name, ProbeStatus Status, string Detail);

public static class Demo02_HealthChecks
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo02 Health checks --");

        async Task<HealthReport> Liveness()
        {
            await Task.Yield();
            return new HealthReport("self", ProbeStatus.Healthy,
                $"EntitleX.Api utc={DateTimeOffset.UtcNow:o}");
        }

        async Task<HealthReport> Provider(string name, bool up)
        {
            await Task.Delay(5);
            return up
                ? new HealthReport(name, ProbeStatus.Healthy, "GET /health 200")
                : new HealthReport(name, ProbeStatus.Unhealthy, "GET /health failed");
        }

        var live = await Liveness();
        var cs = await Provider("cloudshield", up: true);
        var ng = await Provider("networkguard", up: false);

        Console.WriteLine($"  /health          → {live.Status} ({live.Detail})");
        Console.WriteLine($"  /health/providers→ {cs.Name}={cs.Status}, {ng.Name}={ng.Status}");
        Console.WriteLine("  Orchestrators (ECS/K8s): restart on liveness fail;");
        Console.WriteLine("  pull from LB on readiness fail; providers check is diagnostic.");
        Console.WriteLine("  Paths: Program.cs, ProviderHealthCheck.cs, simulators */Program.cs\n");
    }
}

// =============================================================================
// 3. Graceful degradation — partial success aggregation
// =============================================================================
// Real: EntitlementAggregationService — fan-out; IsPartial when any provider fails.
// Does NOT open a platform DB transaction across remote HTTP.

public sealed record ProviderContribution(
    string Provider, bool Succeeded, int Count, string? Error, TimeSpan Duration);

public sealed record AggregationResult(
    string CustomerId,
    string CorrelationId,
    IReadOnlyList<string> Entitlements,
    IReadOnlyList<ProviderContribution> Providers,
    bool IsPartial);

public static class Demo03_GracefulDegradation
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo03 Graceful degradation (aggregation) --");

        async Task<ProviderContribution> QueryAsync(string provider, bool fail)
        {
            var sw = Stopwatch.StartNew();
            await Task.Delay(20);
            if (fail)
            {
                return new ProviderContribution(provider, false, 0,
                    "circuit open / 503", sw.Elapsed);
            }
            return new ProviderContribution(provider, true, 2, null, sw.Elapsed);
        }

        var parts = await Task.WhenAll(
            QueryAsync("CloudShield", fail: false),
            QueryAsync("NetworkGuard", fail: true));

        var entitlements = parts.Where(p => p.Succeeded)
            .SelectMany(_ => new[] { "ent-cs-1", "ent-cs-2" })
            .ToList();

        var result = new AggregationResult(
            "cust-1", "corr-demo", entitlements, parts, IsPartial: parts.Any(p => !p.Succeeded));

        Console.WriteLine($"  entitlements returned: {result.Entitlements.Count}");
        Console.WriteLine($"  IsPartial={result.IsPartial} (UI shows provider error banner)");
        foreach (var p in result.Providers)
            Console.WriteLine($"    {p.Provider}: ok={p.Succeeded} err={p.Error} dur={p.Duration.TotalMilliseconds:n0}ms");
        Console.WriteLine("  Reliability principle: degrade read paths; never invent data.\n");
    }
}

// =============================================================================
// 4. Timeouts + retries + CB — ops view of Polly pipelines
// =============================================================================
public static class Demo04_TimeoutsRetriesCb
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo04 Timeouts / retries / CB (ops) --");
        Console.WriteLine("""
          GET clients (discovery, recon status):
            Timeout → Retry(exponential+jitter, GET-only) → CircuitBreaker → Bulkhead

          POST command clients (license extension apply):
            Timeout → CircuitBreaker → Bulkhead
            *** no retry ***  (failure-scenarios.md explicit non-goal)

          Ops signals when CB opens:
            * entitlex_circuit_breaker_transitions_total{state="opened"}
            * provider latency histogram spikes then drops (fail-fast)
            * aggregation IsPartial=true for that provider
            * recon probes also fail → keep Uncertain (no blind POST)

          Page when: CB open > N minutes AND reconciliation backlog growing.
        """);
        await Task.Delay(1);
        Console.WriteLine("  Config: ProviderOptions (Timeout, RetryCount, CB ratios, bulkhead)");
        Console.WriteLine("  Code:   ProviderHttpClientRegistration.cs\n");
    }
}

// =============================================================================
// 5. Token validation & JWT RBAC
// =============================================================================
// Policies (system-design.md):
//   CanCreateRequests, CanDecideApprovals, CanReadApprovals, CanManageReconciliation
public static class Demo05_TokenValidation
{
    public static void Run()
    {
        Console.WriteLine("-- Demo05 Token validation / JWT RBAC --");

        bool ValidateToken(string? bearer, out string role, out string detail)
        {
            role = "";
            if (string.IsNullOrWhiteSpace(bearer) || !bearer.StartsWith("Bearer "))
            {
                detail = "missing bearer";
                return false;
            }
            // Demo only — real stack: JwtBearer + JwtOptions.SigningKey (≥32 chars)
            var token = bearer["Bearer ".Length..];
            if (token == "rm")
            {
                role = "RenewalManager";
                detail = "ok";
                return true;
            }
            if (token == "admin")
            {
                role = "PlatformAdmin";
                detail = "ok";
                return true;
            }
            detail = "invalid signature / expired (demo)";
            return false;
        }

        bool Authorize(string role, string policy) => policy switch
        {
            "CanDecideApprovals" => role is "RenewalManager" or "DealDeskManager" or "PlatformAdmin",
            "CanManageReconciliation" => role is "PlatformAdmin",
            "CanCreateRequests" => role is "RenewalRepresentative" or "PlatformAdmin",
            _ => false
        };

        foreach (var (tok, policy) in new[]
                 {
                     ("Bearer rm", "CanDecideApprovals"),
                     ("Bearer rm", "CanManageReconciliation"),
                     ("Bearer admin", "CanManageReconciliation"),
                     ("Bearer nope", "CanCreateRequests")
                 })
        {
            var ok = ValidateToken(tok, out var role, out var detail);
            var allowed = ok && Authorize(role, policy);
            Console.WriteLine($"  {tok} policy={policy} → authn={ok}/{detail} authz={allowed}");
        }

        Console.WriteLine("  Handlers re-check roles (retry / manual reconcile) so MediatR");
        Console.WriteLine("  cannot bypass endpoint policies. Dev tokens: MapDevTokenEndpoints.\n");
    }
}

// =============================================================================
// 6. Correlation IDs & tracing
// =============================================================================
// CorrelationIdMiddleware: X-Correlation-ID header, Serilog enricher
// OpenTelemetry: W3C traceparent → Jaeger :16686
public static class Demo06_CorrelationAndTracing
{
    public static void Run()
    {
        Console.WriteLine("-- Demo06 Correlation IDs & tracing --");
        var correlationId = Guid.NewGuid().ToString("N");
        using var activity = new Activity("entitlex.demo.fulfillment");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        activity?.AddTag("entitlex.correlation_id", correlationId);
        activity?.AddTag("entitlex.request_id", Guid.NewGuid().ToString("D"));

        Console.WriteLine($"  X-Correlation-ID = {correlationId}");
        Console.WriteLine($"  W3C Activity.Id  = {activity?.Id}");
        Console.WriteLine("  Join logs by CorrelationId; join traces by trace id in Jaeger.");
        Console.WriteLine("  Outbound: CorrelationIdDelegatingHandler on provider HttpClients.");
        Console.WriteLine("  Sources: EntitleX.Messaging / Saga / Outbox / Reconciliation / Providers\n");
        activity?.Stop();
    }
}

// =============================================================================
// 7. DLQ — SQS maxReceiveCount 5
// =============================================================================
public static class Demo07_SqsDlq
{
    public static void Run()
    {
        Console.WriteLine("-- Demo07 SQS DLQ --");
        Console.WriteLine("""
          Queue: entitlex-fulfillment
          DLQ:   entitlex-fulfillment-dlq
          Redrive: maxReceiveCount = 5  (docs/event-contracts.md, LocalStack init)

          Path:
            OutboxPublisher → SQS
            FulfillmentWorker receives; if handler throws repeatedly,
            SQS redrives to DLQ after 5 receives.

          RUNBOOK — DLQ depth > 0:
            1. Capture message bodies (messageId, requestId, correlationId).
            2. Check Jaeger for the correlation / trace.
            3. Classify: poison payload vs dependency outage vs bug.
            4. If dependency: fix deps, redrive carefully (idempotent consumers!).
            5. If poison payload: fix producer; do NOT infinite redrive.
            6. Verify processed_messages / saga state after redrive.
        """);
    }
}

// =============================================================================
// 8. Poison outbox
// =============================================================================
public static class Demo08_PoisonOutbox
{
    public static void Run()
    {
        Console.WriteLine("-- Demo08 Poison outbox --");
        var attemptCount = 0;
        const int maxAttempts = 5;
        string status = "Pending";
        string? lastError = null;

        while (status is "Pending" or "Processing")
        {
            status = "Processing";
            attemptCount++;
            var publishOk = attemptCount >= 99; // simulate persistent failure
            if (publishOk)
            {
                status = "Published";
                break;
            }
            lastError = "LocalStack SQS 503 / network";
            if (attemptCount >= maxAttempts)
            {
                status = "Poison";
                break;
            }
            // exponential backoff → AvailableAtUtc in future (OutboxPublisherService)
            status = "Pending";
        }

        Console.WriteLine($"  final status={status} attempts={attemptCount} err={lastError}");
        Console.WriteLine("""
          RUNBOOK — Poison rows:
            1. SELECT * FROM outbox_messages WHERE status='Poison' ORDER BY created_at_utc;
            2. Inspect last_error, destination, payload_json, correlation_id.
            3. Fix root cause (IAM, queue name, serialization, LocalStack).
            4. Either:
               a) Replay by resetting to Pending with attempt_count=0 (careful), or
               b) Manually publish once + mark Published after verifying no duplicate effect.
            5. Consumers are idempotent (processed_messages) — still prefer once-only ops.
          Metric: entitlex_retries_total{source="outbox"}
        """);
        Console.WriteLine();
    }
}

// =============================================================================
// 9. Observability stack
// =============================================================================
public static class Demo09_ObservabilityStack
{
    public static void Run()
    {
        Console.WriteLine("-- Demo09 Observability (OTel → Jaeger, Prometheus) --");
        Console.WriteLine("""
          Traces: OTLP gRPC → Jaeger http://localhost:4317  UI :16686
          Metrics scrape:
            API / simulators     GET /metrics  (:5080–5082)
            OutboxPublisher      http://localhost:9180/metrics
            FulfillmentWorker    :9181
            ReconciliationWorker :9182
            NotificationService  :9183
          Grafana: :3001 (admin/admin) — EntitleX Overview dashboard
          Logs: Serilog console; CloudWatch path in observability.md for AWS

          Alert ideas (portfolio):
            * outbox_backlog > 500 for 10m
            * reconciliation_backlog > 100 for 15m
            * sqs_approximate_depth > 1000
            * circuit_breaker opened for CloudShield OR NetworkGuard
            * poison outbox count increased
            * approval latency p95 SLO burn
        """);
    }
}

// =============================================================================
// 10. Canary / deploy notes for ECS
// =============================================================================
// Real: docs/deployment.md, infra/terraform (enable_ecs_services gated)
public static class Demo10_CanaryEcsNotes
{
    public static void Run()
    {
        Console.WriteLine("-- Demo10 Canary / ECS deploy notes --");
        Console.WriteLine("""
          Local primary footprint: Docker Compose deps + dotnet run processes.
          Optional AWS: ECS Fargate tasks for API / workers / simulators.

          Canary checklist (when enable_ecs_services=true):
            1. Images built from infra/docker/images/Dockerfile.*
            2. Secrets present in Secrets Manager (Jwt signing key, connection strings)
               — never bake secrets into images.
            3. Deploy canary task set at 10% (or 1 task) behind ALB.
            4. Watch:
                 /health/ready on canary
                 5xx ratio + entitlex_fulfillment_outcomes_total
                 outbox backlog (publisher canary)
                 provider CB transitions
            5. Soak; promote to 100% or hard rollback (previous task definition).
            6. Migrations: run controlled migrate job BEFORE shifting traffic
               (API MayMigrateOnStartup is a lab convenience — prefer explicit jobs).
            7. Lambdas (ApprovalReminder / ExpirationNotification) via EventBridge —
               deploy independently; window_key tables keep them idempotent.

          Cost: tear down idle labs; NAT is expensive — see infra/terraform/COST.md
        """);
    }
}

// =============================================================================
// 11. Runbooks (comment playbooks) — interview + on-call
// =============================================================================
public static class Demo11_Runbooks
{
    public static void Run()
    {
        Console.WriteLine("-- Demo11 Runbooks --");
        Console.WriteLine("""
          =================================================================
          RUNBOOK A — Fulfillment stuck InFulfillment / Processing
          =================================================================
          Symptoms: request status InFulfillment; saga Processing; backlog↑
          Steps:
            1. GET /api/v1/license-extension-requests/{id}/saga
            2. Check item outcomes (Pending vs Uncertain vs Failed)
            3. Metrics: outbox_backlog, sqs depth, kafka lag hooks
            4. If outbox Pending aging: inspect OutboxPublisher logs / Poison
            5. If SQS depth↑ but worker idle: scale FulfillmentWorker / check CB
            6. If items Uncertain: POST admin reconciliation (PlatformAdmin)
            7. If definitive Failed: selective retry with Idempotency-Key
          Do NOT: blind re-POST provider extensions; demote Succeeded items

          =================================================================
          RUNBOOK B — One provider down during entitlement search
          =================================================================
          Symptoms: aggregation IsPartial=true; /health/providers unhealthy
          Steps:
            1. Confirm simulator/process health (:5081 / :5082)
            2. Check CB state metrics for that provider
            3. UI continues with partial entitlements — communicate degraded mode
            4. Block creating extensions for the down provider if discovery empty
            5. After recovery: CB half-open → closed; re-search

          =================================================================
          RUNBOOK C — Selective retry 409 Conflict
          =================================================================
          Cause: retry_item_claims unique (source_request_id, logical_operation_id)
          Steps:
            1. Another operator/client already claimed those failed items
            2. GET existing retry via api_idempotency_records if same key
            3. If different key: wait for in-flight retry or inspect claims table
            4. Never delete claims casually — lineage matters for audit

          =================================================================
          RUNBOOK D — Reconciliation keeps ItemStillUnknown
          =================================================================
          Cause: provider NotFound OR probe errors (failure-scenarios F2/F4)
          Steps:
            1. Verify provider received Kafka command (consumer logs)
            2. Query simulator AppliedLicenseOperation by LogicalOperationId
            3. If Applied locally missing: CorrectItemSucceededFromReconciliation path
            4. If truly never applied: ops may authorize selective retry ONLY after
               clearing Uncertain deliberately — still prefer confirmed Failed
            5. Audit events: Reconciliation.ItemStillUnknown / MismatchDetected

          =================================================================
          RUNBOOK E — Approval timeouts / Escalated
          =================================================================
          Cause: ApprovalPending timeout (saga) or reminder Lambda terminal state
          Steps:
            1. Saga state Escalated (NOT auto-approved)
            2. Human resumes: RM / Deal Desk decision endpoints
            3. Check Mailpit/SES for reminder emails
            4. approval_reminder_records.window_key prevents duplicate sends

          =================================================================
          RUNBOOK F — Security: token / RBAC incidents
          =================================================================
          Steps:
            1. Rotate Jwt:SigningKey (Secrets Manager) → bounce API tasks
            2. Invalidate long-lived tokens; disable EnableDevTokenEndpoint outside Dev
            3. Audit decide/retry/reconcile actors via audit_events + correlation ids
            4. Confirm policies still map to least privilege roles
        """);

        // Tiny executable assertion that partial aggregation stays available
        var degraded = true;
        var stillServingReads = degraded && true;
        Console.WriteLine($"  degraded-mode reads still served? {stillServingReads}");
        Console.WriteLine("  Remember: reliability is forward recovery + observability + runbooks,");
        Console.WriteLine("  not wishful exactly-once networking.");
    }
}


// =============================================================================
// 12. SLI burn simulation (error budget intuition)
// =============================================================================
public static class Demo12_SliBurnSimulation
{
    public static void Run()
    {
        Console.WriteLine("-- Demo12 SLI burn simulation --");
        // 30-day minutes = 43200; 99.9% ⇒ 43.2 minutes budget
        const double monthMinutes = 30 * 24 * 60;
        const double slo = 0.999;
        var budgetMinutes = monthMinutes * (1 - slo);
        var incidentMinutes = 12.0; // bad canary
        var remaining = budgetMinutes - incidentMinutes;
        Console.WriteLine($"  monthly budget≈{budgetMinutes:n1}m; incident={incidentMinutes}m; remaining≈{remaining:n1}m");
        Console.WriteLine(remaining < budgetMinutes * 0.5
            ? "  Action: freeze risky deploys; focus on reliability work."
            : "  Action: continue with caution.");
        Console.WriteLine();
    }
}

// =============================================================================
// 13. Dependency criticality matrix
// =============================================================================
public static class Demo13_DependencyMatrix
{
    public static void Run()
    {
        Console.WriteLine("-- Demo13 Dependency criticality matrix --");
        var rows = new[]
        {
            ("Postgres entitlex_platform", "critical", "API + all workers halt"),
            ("SQS entitlex-fulfillment", "critical-async", "approvals ok; fulfillment stalls; outbox backlog↑"),
            ("Kafka broker", "critical-async", "commands/results stall; recon may clarify later"),
            ("CloudShield.Api", "degrade-reads", "aggregation partial; CB may open; recon limited"),
            ("NetworkGuard.Api", "degrade-reads", "same as CloudShield"),
            ("Mailpit/SES", "best-effort", "reminders/expiry emails delayed"),
            ("Jaeger/Prometheus", "telemetry", "fly blind — do not block requests"),
        };
        foreach (var (dep, class_, impact) in rows)
            Console.WriteLine($"  {dep,-28} {class_,-16} {impact}");
        Console.WriteLine("""

          ECS task advice:
            * Keep telemetry sidecars/exporters failure-isolated (never crash app on export fail).
            * Readiness should require Postgres; should NOT require Jaeger.
            * Provider health is exposed on /health/providers separately from /health/ready
              so a single vendor outage does not eject the control plane from the LB.
        """);
    }
}


// =============================================================================
// 14. Load shedding & queue reliability
// =============================================================================
public static class Demo14_LoadSheddingAndQueues
{
    public static void Run()
    {
        Console.WriteLine("-- Demo14 Load shedding & queues --");
        Console.WriteLine("""
          When fulfillment fan-in exceeds provider capacity:
            1. Bulkhead queue fills → shed new POSTs / GETs for that provider
            2. Outbox remains durable — backlog gauge rises (backpressure signal)
            3. SQS depth rises; DLQ only after maxReceiveCount (poison vs overload)
            4. Prefer slowing producers / scaling workers over dropping approvals
            5. Approvals can continue — sync path is not the same failure domain

          Capacity knobs:
            Outbox BatchSize / LockDurationSeconds (MessagingOptions)
            Provider BulkheadMaxParallelization / BulkheadQueueLimit
            FulfillmentWorker concurrency / Kafka consumer max.poll
            ECS desired_count for publisher & fulfillment services
        """);
    }
}

// =============================================================================
// 15. Security ↔ reliability intersection
// =============================================================================
public static class Demo15_SecurityReliability
{
    public static void Run()
    {
        Console.WriteLine("-- Demo15 Security ↔ reliability --");
        Console.WriteLine("""
          Auth outages ARE availability incidents:
            * Jwt signing key mismatch after rotate → mass 401 → SLO burn
            * Fix: dual-key validation window during rotation (ideal) or fast bounce
            * Dev token endpoint must be off outside Development

          Least privilege reduces blast radius:
            * RenewalRepresentative cannot reconcile or decide Deal Desk stages
            * PlatformAdmin for manual recon — audited via audit_events
            * Correlation IDs on decisions support forensic timelines

          Secrets reliability:
            * Compose: appsettings/env; AWS: Secrets Manager shells (deployment.md)
            * Missing secret should fail readiness, not silently use defaults in prod
        """);
    }
}


// =============================================================================
// 16. Synthetic checks (black-box probes)
// =============================================================================
public static class Demo16_SyntheticChecks
{
    public static void Run()
    {
        Console.WriteLine("-- Demo16 Synthetic checks --");
        var checks = new[]
        {
            ("GET /health", "expect 200"),
            ("GET /health/ready", "expect Healthy when Postgres up"),
            ("GET /health/providers", "may be Degraded if a simulator down"),
            ("GET /metrics", "expect entitlex_* series"),
            ("POST create+approve auto-band", "expect outbox Pending→Published"),
            ("admin ops summary", "PlatformAdmin — backlog gauges"),
        };
        foreach (var (check, expect) in checks)
            Console.WriteLine($"  [ ] {check,-40} // {expect}");
        Console.WriteLine("""

          Wire synthetics into CI smoke or a 1-minute EventBridge/Lambda ping.
          Alert on consecutive failures, not single blips (compose restarts).
          Correlate synthetic failures with Jaeger empty traces vs Postgres down.
        """);
    }
}

// =============================================================================
// Appendix — reliability property checklist (tick in interviews)
// =============================================================================
// [x] Liveness vs readiness vs dependency health split
// [x] Partial success on multi-provider reads
// [x] Timeouts on all remote calls; retries only when idempotent
// [x] Circuit breaker + bulkhead isolation per provider
// [x] Transactional outbox; no dual-write to SQS in request thread
// [x] DLQ + poison outbox with operator playbooks
// [x] Idempotent consumers and provider apply
// [x] Reconciliation before blind retry
// [x] Correlation + traces + low-cardinality metrics
// [x] JWT authn/authz with handler re-checks
// [x] Canary-friendly deploy story on ECS
// [x] UTC clocks, leases, and backlog gauges for backpressure
//
// Related reading order:
//   docs/system-design.md → failure-scenarios.md → observability.md → deployment.md
//   guides/09 (Postgres locks) → 11 (patterns) → this file (ops)
//
// END guide 12
