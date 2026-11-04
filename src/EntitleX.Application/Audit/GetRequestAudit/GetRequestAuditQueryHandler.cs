using EntitleX.Application.Abstractions;
using EntitleX.BuildingBlocks.Ids;
using MediatR;

namespace EntitleX.Application.Audit.GetRequestAudit;

public sealed class GetRequestAuditQueryHandler
    : IRequestHandler<GetRequestAuditQuery, RequestAuditPageDto>
{
    private readonly IAuditEventStore _audit;

    public GetRequestAuditQueryHandler(IAuditEventStore audit)
    {
        _audit = audit;
    }

    public async Task<RequestAuditPageDto> Handle(
        GetRequestAuditQuery query,
        CancellationToken cancellationToken)
    {
        var events = await _audit.ListByRequestAsync(
            RequestId.From(query.RequestId),
            cancellationToken);

        return new RequestAuditPageDto(
            query.RequestId,
            events.Select(e => new AuditEventDto(
                e.Id,
                e.EventType,
                e.RequestId?.Value,
                e.LogicalOperationId?.Value,
                e.Actor,
                e.Detail,
                e.CorrelationId,
                e.OccurredAtUtc)).ToList());
    }
}
