using System.Diagnostics.Metrics;

namespace EntitleX.BuildingBlocks.Observability;

/// <summary>
/// Shared meters and instruments. Labels are low-cardinality only
/// (provider, message_type, destination_kind, state, outcome, queue/topic name).
/// Never attach request/message/logical-operation IDs.
/// </summary>
public static class EntitleXMeters
{
    public const string MeterName = "EntitleX";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Histogram<double> ApprovalLatencySeconds =
        Meter.CreateHistogram<double>(
            "entitlex_approval_latency_seconds",
            unit: "s",
            description: "Time from approval case open to decision recorded.");

    public static readonly Counter<long> FulfillmentOutcomes =
        Meter.CreateCounter<long>(
            "entitlex_fulfillment_outcomes_total",
            description: "Fulfillment terminal outcomes (success, partial, failed).");

    public static readonly Counter<long> RetryAttempts =
        Meter.CreateCounter<long>(
            "entitlex_retries_total",
            description: "Retry attempts (outbox publish, selective retry claims).");

    public static readonly Counter<long> OutboxClaimed =
        Meter.CreateCounter<long>("entitlex_outbox_claimed_total", description: "Outbox rows claimed.");

    public static readonly Counter<long> OutboxPublished =
        Meter.CreateCounter<long>("entitlex_outbox_published_total", description: "Outbox rows published.");

    public static readonly Counter<long> OutboxPoison =
        Meter.CreateCounter<long>("entitlex_outbox_poison_total", description: "Outbox rows marked poison.");

    public static readonly Counter<long> OutboxPublishFailures =
        Meter.CreateCounter<long>("entitlex_outbox_publish_failures_total", description: "Outbox publish failures.");

    public static readonly Counter<long> SagaTransitions =
        Meter.CreateCounter<long>(
            "entitlex_saga_transitions_total",
            description: "Saga state transitions.");

    public static readonly Counter<long> ReconciliationActions =
        Meter.CreateCounter<long>(
            "entitlex_reconciliation_actions_total",
            description: "Reconciliation item actions.");

    public static readonly Counter<long> CircuitBreakerTransitions =
        Meter.CreateCounter<long>(
            "entitlex_circuit_breaker_transitions_total",
            description: "Provider HttpClient circuit breaker state transitions.");

    public static readonly Histogram<double> ProviderLatencySeconds =
        Meter.CreateHistogram<double>(
            "entitlex_provider_latency_seconds",
            unit: "s",
            description: "Outbound provider HTTP call latency.");

    public static readonly Histogram<double> MessagingLatencySeconds =
        Meter.CreateHistogram<double>(
            "entitlex_messaging_latency_seconds",
            unit: "s",
            description: "SQS/Kafka produce or consume handling latency.");

    public static readonly Counter<long> MessagingOperations =
        Meter.CreateCounter<long>(
            "entitlex_messaging_operations_total",
            description: "Messaging produce/consume operations.");

    /// <summary>Last observed SQS approximate depth (hook; refreshed by probe).</summary>
    public static long SqsApproximateDepth;

    /// <summary>Last observed Kafka consumer lag hook (best-effort).</summary>
    public static long KafkaConsumerLagHook;

    public static long OutboxBacklogValue;
    public static long ReconciliationBacklogValue;

    static EntitleXMeters()
    {
        Meter.CreateObservableGauge(
            "entitlex_outbox_backlog",
            () => OutboxBacklogValue,
            description: "Pending/retryable outbox messages (polled gauge).");

        Meter.CreateObservableGauge(
            "entitlex_reconciliation_backlog",
            () => ReconciliationBacklogValue,
            description: "Sagas requiring reconciliation (polled gauge).");

        Meter.CreateObservableGauge(
            "entitlex_sqs_approximate_depth",
            () => SqsApproximateDepth,
            description: "SQS ApproximateNumberOfMessages hook for fulfillment queue.");

        Meter.CreateObservableGauge(
            "entitlex_kafka_consumer_lag_hook",
            () => KafkaConsumerLagHook,
            description: "Kafka consumer lag hook (best-effort; 0 when unavailable).");
    }
}
