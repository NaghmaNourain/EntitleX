using EntitleX.Application.Insights.Models;
using MediatR;

namespace EntitleX.Application.Insights.ReconcileRca;

public sealed record ReconcileRcaQuery(Guid RequestId) : IRequest<RequestInsightDto>;
