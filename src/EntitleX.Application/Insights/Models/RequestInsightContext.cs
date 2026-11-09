using EntitleX.Application.Abstractions;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.Insights.Models;

public sealed record OutboxPoisonInsight(
    Guid Id,
    string MessageType,
    string Destination,
    string? LastError,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    Guid? LogicalOperationId);

public sealed record InsightItemSnapshot(
    Guid RequestItemId,
    string EntitlementId,
    Guid LogicalOperationId,
    string? Provider,
    SagaItemOutcomeStatus? SagaStatus,
    LocalFulfillmentOutcome? Ledger,
    ProviderOperationStatus? Probe);

public sealed class RequestInsightContext
{
    public required Guid RequestId { get; init; }

    public LicenseExtensionRequest? Request { get; init; }

    public ApprovalCase? Approval { get; init; }

    public LicenseExtensionSaga? Saga { get; init; }

    public IReadOnlyList<InsightItemSnapshot> Items { get; init; } = [];

    public IReadOnlyList<OutboxPoisonInsight> PoisonOutbox { get; init; } = [];

    public IReadOnlyList<AuditEventRecord> RecentAudit { get; init; } = [];

    public IReadOnlyList<string> BuilderNotes { get; init; } = [];
}
