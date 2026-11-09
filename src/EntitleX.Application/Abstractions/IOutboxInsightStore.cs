using EntitleX.Application.Insights.Models;
using EntitleX.SharedKernel.Ids;

namespace EntitleX.Application.Abstractions;

public interface IOutboxInsightStore
{
    Task<IReadOnlyList<OutboxPoisonInsight>> ListPoisonByRequestAsync(
        RequestId requestId,
        int take = 25,
        CancellationToken cancellationToken = default);
}
