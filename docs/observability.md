# EntitleX observability (Phase 11)

## Tracing (OpenTelemetry → Jaeger)

- All ASP.NET hosts, workers, and simulator APIs call `AddEntitleXOpenTelemetry`.
- Exporter: OTLP gRPC to Jaeger (`http://localhost:4317` by default).
- Propagation: **W3C** `traceparent` / `tracestate` (`Activity.DefaultIdFormat = W3C`).
- Instrumented:
  - ASP.NET Core + HttpClient + EF Core (AspNetCore hosts)
  - Custom sources: `EntitleX.Messaging`, `EntitleX.Saga`, `EntitleX.Outbox`, `EntitleX.Reconciliation`, `EntitleX.Providers`
  - SQS/Kafka produce & consume; saga transitions; outbox publish; reconciliation passes
- UI: Jaeger `http://localhost:16686`

Correlation continues via `X-Correlation-ID` (Serilog + outbound HTTP). Prefer joining on W3C trace id in Jaeger; correlation id remains for business logs.

## Metrics (Prometheus + Grafana)

| Host | Scrape |
|------|--------|
| EntitleX.Api / simulators | `GET /metrics` (ports 5080–5082) |
| OutboxPublisher | HttpListener `http://localhost:9180/metrics` |
| FulfillmentWorker | `http://localhost:9181/metrics` |
| ReconciliationWorker | `http://localhost:9182/metrics` |
| NotificationService | `http://localhost:9183/metrics` |

Prometheus Compose job scrapes `host.docker.internal` (see `infra/docker/prometheus/prometheus.yml`).

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

Grafana: `http://localhost:3001` (admin/admin) — provisioned **EntitleX Overview** dashboard.

## Serilog

Structured console logs with enrichers: `Service`, environment, machine name, `CorrelationId` (API middleware).

### CloudWatch (AWS path)

Local Compose uses console sinks. For AWS deployments:

1. Keep the same property names (`Service`, `CorrelationId`, `RequestId` when present as scalar properties — not metric labels).
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
