using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.Insights.Abstractions;
using EntitleX.Application.Insights.Models;
using EntitleX.Domain.Identity;
using EntitleX.SharedKernel.Ids;
using MediatR;

namespace EntitleX.Application.Insights.ReconcileRca;

public sealed class ReconcileRcaQueryHandler
    : IRequestHandler<ReconcileRcaQuery, RequestInsightDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly IRequestInsightContextBuilder _builder;
    private readonly DeterministicInsightComposer _composer;
    private readonly RequestInsightNarrator _narrator;

    public ReconcileRcaQueryHandler(
        ICurrentUser currentUser,
        IRequestInsightContextBuilder builder,
        DeterministicInsightComposer composer,
        RequestInsightNarrator narrator)
    {
        _currentUser = currentUser;
        _builder = builder;
        _composer = composer;
        _narrator = narrator;
    }

    public async Task<RequestInsightDto> Handle(
        ReconcileRcaQuery query,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(EntitleXRoles.PlatformAdmin))
        {
            throw new ForbiddenException(
                "Reconciliation RCA requires the PlatformAdmin role.");
        }

        var requestId = RequestId.From(query.RequestId);
        var context = await _builder.BuildAsync(
            requestId,
            includeLiveProbes: true,
            cancellationToken);
        var composed = _composer.Compose(context, "Rca");
        return await _narrator.NarrateAsync(
            query.RequestId,
            "Rca",
            composed,
            cancellationToken);
    }
}
