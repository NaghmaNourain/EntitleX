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

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = Math.Clamp(query.PageSize > 0 ? query.PageSize : (query.Take ?? 50), 1, 100);

        // Store still uses Take(limit) / keyset; totalCount comes from the full request ledger.
        var all = await _audit.ListByRequestAsync(requestId, cancellationToken);
        var totalCount = all.Count;

        IReadOnlyList<AuditEventRecord> events;
        if (query.AfterOccurredAtUtc is not null && query.AfterId is not null)
        {
            events = await _audit.ListByRequestKeysetAsync(
                requestId,
                pageSize,
                query.AfterOccurredAtUtc,
                query.AfterId,
                cancellationToken);
        }
        else
        {
            var skip = (page - 1) * pageSize;
            events = all.Skip(skip).Take(pageSize).ToList();
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
        if (dtos.Count == pageSize && dtos.Count > 0)
        {
            var last = dtos[^1];
            nextOccurred = last.OccurredAtUtc;
            nextId = last.Id;
        }

        return new RequestAuditPageDto(
            query.RequestId,
            dtos,
            nextOccurred,
            nextId,
            page,
            pageSize,
            totalCount);
    }
}
