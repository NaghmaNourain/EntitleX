using EntitleX.Application.Insights.Models;
using MediatR;

namespace EntitleX.Application.Insights.ExplainRequest;

public sealed record ExplainRequestQuery(Guid RequestId) : IRequest<RequestInsightDto>;
