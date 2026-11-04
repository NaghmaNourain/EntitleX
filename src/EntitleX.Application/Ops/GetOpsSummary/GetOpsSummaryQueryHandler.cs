using EntitleX.Application.Abstractions;
using EntitleX.Domain.Approval;
using MediatR;

namespace EntitleX.Application.Ops.GetOpsSummary;

public sealed class GetOpsSummaryQueryHandler
    : IRequestHandler<GetOpsSummaryQuery, OpsSummaryDto>
{
    private readonly IApprovalCaseRepository _approvals;
    private readonly ILicenseExtensionSagaRepository _sagas;

    public GetOpsSummaryQueryHandler(
        IApprovalCaseRepository approvals,
        ILicenseExtensionSagaRepository sagas)
    {
        _approvals = approvals;
        _sagas = sagas;
    }

    public async Task<OpsSummaryDto> Handle(
        GetOpsSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, 100);

        var pending = await _approvals.ListPendingAsync(500, cancellationToken);
        var recon = await _sagas.ListRequiringReconciliationAsync(take, cancellationToken);

        return new OpsSummaryDto(
            DateTimeOffset.UtcNow,
            pending.Count,
            recon.Count,
            pending.Take(take).Select(c => new PendingApprovalOpsItemDto(
                c.RequestId.Value,
                c.Band.ToString(),
                c.CurrentStage is { } stage
                    ? ApprovalRules.RoleName(stage.RequiredRole)
                    : null,
                c.CreatedAtUtc)).ToList(),
            recon.Select(s => new ReconciliationOpsItemDto(
                s.RequestId.Value,
                s.State.ToString(),
                s.SucceededItemCount,
                s.FailedItemCount,
                s.PendingItemCount,
                s.UpdatedAtUtc)).ToList());
    }
}
