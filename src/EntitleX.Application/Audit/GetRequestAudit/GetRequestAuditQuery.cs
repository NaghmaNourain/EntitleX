using MediatR;

namespace EntitleX.Application.Audit.GetRequestAudit;

public sealed record GetRequestAuditQuery(
    Guid RequestId,
    int? Take = null,
    DateTimeOffset? AfterOccurredAtUtc = null,
    Guid? AfterId = null,
    int Page = 1,
    int PageSize = 50) : IRequest<RequestAuditPageDto>;

public sealed record RequestAuditPageDto(
    Guid RequestId,
    IReadOnlyList<AuditEventDto> Events,
    DateTimeOffset? NextOccurredAtUtc = null,
    Guid? NextId = null,
    int Page = 1,
    int PageSize = 50,
    int TotalCount = 0);

public sealed record AuditEventDto(
    Guid Id,
    string EventType,
    Guid? RequestId,
    Guid? LogicalOperationId,
    string Actor,
    string Detail,
    string? CorrelationId,
    DateTimeOffset OccurredAtUtc);
