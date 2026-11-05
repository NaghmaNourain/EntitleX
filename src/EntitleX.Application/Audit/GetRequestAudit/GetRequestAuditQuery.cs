using MediatR;

namespace EntitleX.Application.Audit.GetRequestAudit;

public sealed record GetRequestAuditQuery(
    Guid RequestId,
    int? Take = null,
    DateTimeOffset? AfterOccurredAtUtc = null,
    Guid? AfterId = null) : IRequest<RequestAuditPageDto>;

public sealed record RequestAuditPageDto(
    Guid RequestId,
    IReadOnlyList<AuditEventDto> Events,
    DateTimeOffset? NextOccurredAtUtc = null,
    Guid? NextId = null);

public sealed record AuditEventDto(
    Guid Id,
    string EventType,
    Guid? RequestId,
    Guid? LogicalOperationId,
    string Actor,
    string Detail,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc);
