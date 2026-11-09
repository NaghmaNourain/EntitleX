namespace EntitleX.Application.Insights.Models;

public sealed record RequestInsightDto(
    Guid RequestId,
    string InsightKind,
    string Summary,
    string RecommendedAction,
    string NarrationMode,
    string? Model,
    IReadOnlyList<InsightCitation> Citations,
    RequestInsightFactsDto Facts);

public sealed record RequestInsightFactsDto(
    string? RequestStatus,
    string? ExtensionNumber,
    string? RequestedBy,
    string? ApprovalStatus,
    string? ApprovalBand,
    string? SagaState,
    int SucceededItemCount,
    int FailedItemCount,
    int PendingItemCount,
    int UncertainItemCount,
    int PoisonOutboxCount,
    int LedgerMismatchCount,
    IReadOnlyList<InsightItemFactDto> Items,
    IReadOnlyList<InsightProbeFactDto> Probes,
    IReadOnlyList<InsightPoisonFactDto> PoisonOutbox,
    IReadOnlyList<InsightAuditFactDto> RecentAudit,
    IReadOnlyList<string> Notes);

public sealed record InsightItemFactDto(
    Guid RequestItemId,
    string EntitlementId,
    Guid LogicalOperationId,
    string? Provider,
    string SagaStatus,
    bool? LedgerSucceeded,
    string? LedgerErrorCode);

public sealed record InsightProbeFactDto(
    Guid LogicalOperationId,
    string Provider,
    string Kind,
    string? ProviderStatus,
    string? Detail);

public sealed record InsightPoisonFactDto(
    Guid OutboxMessageId,
    string MessageType,
    string Destination,
    string? LastError,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc);

public sealed record InsightAuditFactDto(
    Guid Id,
    string EventType,
    string Detail,
    DateTimeOffset OccurredAtUtc);
