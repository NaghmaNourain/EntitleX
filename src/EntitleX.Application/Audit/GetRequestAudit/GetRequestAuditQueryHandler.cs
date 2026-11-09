using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using MediatR;

namespace EntitleX.Application.Audit.GetRequestAudit;

public sealed class GetRequestAuditQueryHandler
    : IRequestHandler<GetRequestAuditQuery, RequestAuditPageDto>
{
    private readonly IAuditEventStore _audit;
    private readonly IResourceAuthorizationService _authorization;

    public GetRequestAuditQueryHandler(
        IAuditEventStore audit,
        IResourceAuthorizationService authorization)
    {
        _audit = audit;
        _authorization = authorization;
    }

    public async Task<RequestAuditPageDto> Handle(
        GetRequestAuditQuery query,
        CancellationToken cancellationToken)
    {
        var requestId = RequestId.From(query.RequestId);
        await _authorization.EnsureCanReadRequestAsync(requestId, cancellationToken);

        IReadOnlyList<AuditEventRecord> events;

        if (query.Take is not null)
        {
            events = await _audit.ListByRequestKeysetAsync(
                requestId,
                query.Take.Value,
                query.AfterOccurredAtUtc,
                query.AfterId,
                cancellationToken);
        }
        else
        {
            events = await _audit.ListByRequestAsync(requestId, cancellationToken);
        }

        var dtos = events.Select(e => new AuditEventDto(
            e.Id,
            e.EventType,
            e.RequestId?.Value,
            e.LogicalOperationId?.Value,
            e.Actor,
            e.Detail,
            e.CorrelationId,
            e.OccurredAtUtc)).ToList();

        DateTimeOffset? nextOccurred = null;
        Guid? nextId = null;
        if (query.Take is not null && dtos.Count == query.Take.Value && dtos.Count > 0)
        {
            var last = dtos[^1];
            nextOccurred = last.OccurredAtUtc;
            nextId = last.Id;
        }

        return new RequestAuditPageDto(query.RequestId, dtos, nextOccurred, nextId);
    }
}
