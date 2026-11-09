# EntitleX observability (Phase 11 + hardening)

## Tracing (OpenTelemetry → Jaeger)

- All ASP.NET hosts, workers, and simulator APIs call `AddEntitleXOpenTelemetry`.
- Exporter: OTLP gRPC to Jaeger (`http://localhost:4317` by default).
- Propagation: **W3C** `traceparent` / `tracestate` (`Activity.DefaultIdFormat = W3C`).
- Instrumented:
  - ASP.NET Core + HttpClient + EF Core (AspNetCore hosts)
  - Custom sources: `EntitleX.Messaging`, `EntitleX.Saga`, `EntitleX.Outbox`, `EntitleX.Reconciliation`, `EntitleX.Providers`
  - SQS/Kafka **produce** injects `traceparent` / `tracestate`
  - SQS/Kafka **consume** extracts parent context and starts a child `Activity` (`messaging.consume …`)
  - Saga transitions; outbox publish; reconciliation passes
- UI: Jaeger `http://localhost:16686`
- Grafana: provisioned **Jaeger** datasource (`http://jaeger:16686`) alongside Prometheus

Business correlation continues via `X-Correlation-ID` (Serilog + outbound HTTP). Prefer joining on W3C `TraceId` in Jaeger; `CorrelationId` remains for business logs.

## Metrics (Prometheus + Grafana)

| Host | Scrape |
|------|--------|
| EntitleX.Api / simulators | `GET /metrics` (ports 5080–5082) |
| OutboxPublisher | HttpListener `http://localhost:9180/metrics` |
| FulfillmentWorker | `http://localhost:9181/metrics` |
| ReconciliationWorker | `http://localhost:9182/metrics` |
| NotificationService | `http://localhost:9183/metrics` |
| ResultsWorker | HttpListener `http://localhost:9184/metrics` |

Prometheus Compose jobs scrape `host.docker.internal` (see `infra/docker/prometheus/prometheus.yml`).

### Metric catalogue (no high-cardinality ID labels)

| Metric | Labels (low-cardinality only) |
|--------|-------------------------------|
| ASP.NET request duration | from AspNetCore instrumentation |
| `entitlex_approval_latency_seconds` | `outcome`, `stage_role` |
| `entitlex_fulfillment_outcomes_total` | `outcome` (`success`/`partial`/`failed`) |
| `entitlex_retries_total` | `source` (`outbox` / `selective_retry`), optional `message_type` |
| `entitlex_outbox_backlog` | gauge |
| `entitlex_reconciliation_backlog` | gauge |
| `entitlex_sqs_approximate_depth` | SQS depth hook |
| `entitlex_kafka_consumer_lag_hook` | lag hook |
| `entitlex_provider_latency_seconds` | `provider`, `method` |
| `entitlex_circuit_breaker_transitions_total` | `provider`, `state` |
| `entitlex_saga_transitions_total` | `from_state`, `to_state` |
| `entitlex_messaging_*` | `transport`, `operation`, `destination`, `success` |

**Never** label by `requestId`, `messageId`, `logicalOperationId`, or raw URLs.

Grafana: `http://localhost:3001` (admin/admin) — provisioned **EntitleX Overview** dashboard (links to Jaeger + Prometheus targets).

## Serilog

Structured console logs with enrichers:

| Property | Source |
|----------|--------|
| `Service` | host registration |
| environment / machine | Serilog environment enrichers |
| `CorrelationId` | `X-Correlation-ID` middleware (Api + CloudShield + NetworkGuard) |
| `TraceId` / `SpanId` | `ActivityTraceEnricher` from `Activity.Current` (when a span is active) |

### CloudWatch (AWS path)

Local Compose uses console sinks. For AWS deployments:

1. Keep the same property names (`Service`, `CorrelationId`, `TraceId`, `SpanId`, `RequestId` when present as scalar properties — not metric labels).
2. Add `Serilog.Sinks.AwsCloudWatch` (or Fluent Bit / FireLens → CloudWatch Logs).
3. Use log groups per service (`/entitlex/api`, `/entitlex/outbox-publisher`, …).
4. Optionally ship OTLP to AWS X-Ray / ADOT collector instead of Jaeger; keep W3C propagation.

Do **not** emit high-cardinality metric dimensions to CloudWatch Metrics EMF for per-request IDs.

## Configuration

```json
"OpenTelemetry": {
  "ServiceName": "EntitleX.Api",
  "OtlpEndpoint": "http://localhost:4317",
  "EnablePrometheusAspNetCore": true,
  "PrometheusHttpListenerPrefixes": "http://localhost:9180/",
  "EnableTracing": true,
  "EnableMetrics": true
}
```

Simulators use the same OTLP endpoint with their own `ServiceName`.

## Lab verification

1. `docker compose up -d` (Jaeger, Prometheus, Grafana healthy).
2. Start **Lab Full Stack** (or API + workers + simulators).
3. Prometheus → Status → Targets: jobs for api/sims/`9180`–`9184` should scrape (UP when hosts are running).
4. Hit a control-plane API request; open Jaeger UI and find service `EntitleX.Api` (and linked messaging spans when fulfillment runs).
5. Grafana `:3001` → dashboard **EntitleX Overview**; Explore → Jaeger datasource.
