using EntitleX.Application.Abstractions;
using EntitleX.Application.Insights.Abstractions;
using EntitleX.Application.Insights.Models;
using EntitleX.SharedKernel.Ids;
using MediatR;

namespace EntitleX.Application.Insights.ExplainRequest;

public sealed class ExplainRequestQueryHandler
    : IRequestHandler<ExplainRequestQuery, RequestInsightDto>
{
    private readonly IResourceAuthorizationService _authorization;
    private readonly IRequestInsightContextBuilder _builder;
    private readonly DeterministicInsightComposer _composer;
    private readonly RequestInsightNarrator _narrator;

    public ExplainRequestQueryHandler(
        IResourceAuthorizationService authorization,
        IRequestInsightContextBuilder builder,
        DeterministicInsightComposer composer,
        RequestInsightNarrator narrator)
    {
        _authorization = authorization;
        _builder = builder;
        _composer = composer;
        _narrator = narrator;
    }

    public async Task<RequestInsightDto> Handle(
        ExplainRequestQuery query,
        CancellationToken cancellationToken)
    {
        var requestId = RequestId.From(query.RequestId);
        await _authorization.EnsureCanReadRequestAsync(requestId, cancellationToken);

        var context = await _builder.BuildAsync(
            requestId,
            includeLiveProbes: true,
            cancellationToken);
        var composed = _composer.Compose(context, "Explain");
        return await _narrator.NarrateAsync(
            query.RequestId,
            "Explain",
            composed,
            cancellationToken);
    }
}
