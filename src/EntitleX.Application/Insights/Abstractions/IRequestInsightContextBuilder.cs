using EntitleX.Application.Insights.Models;
using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.Insights.Abstractions;

public interface IRequestInsightContextBuilder
{
    Task<RequestInsightContext> BuildAsync(
        RequestId requestId,
        bool includeLiveProbes,
        CancellationToken cancellationToken = default);
}
