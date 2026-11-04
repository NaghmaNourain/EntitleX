using System.Diagnostics;

namespace EntitleX.BuildingBlocks.Observability;

/// <summary>Shared ActivitySource names for EntitleX traces (W3C via OpenTelemetry).</summary>
public static class EntitleXActivitySources
{
    public const string MessagingName = "EntitleX.Messaging";
    public const string SagaName = "EntitleX.Saga";
    public const string OutboxName = "EntitleX.Outbox";
    public const string ReconciliationName = "EntitleX.Reconciliation";
    public const string ProvidersName = "EntitleX.Providers";

    public static readonly ActivitySource Messaging = new(MessagingName);
    public static readonly ActivitySource Saga = new(SagaName);
    public static readonly ActivitySource Outbox = new(OutboxName);
    public static readonly ActivitySource Reconciliation = new(ReconciliationName);
    public static readonly ActivitySource Providers = new(ProvidersName);
}
