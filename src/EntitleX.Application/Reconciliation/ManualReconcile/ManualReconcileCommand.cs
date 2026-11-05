using EntitleX.Application.Abstractions;
using MediatR;

namespace EntitleX.Application.Reconciliation.ManualReconcile;

public sealed record ManualReconcileCommand(
    Guid RequestId,
    string? CorrelationId = null) : IRequest<ReconciliationRunResult>;
