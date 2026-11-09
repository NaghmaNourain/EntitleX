using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Exceptions;
using EntitleX.Application.Insights.Abstractions;
using EntitleX.Application.Insights.Models;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using EntitleX.SharedKernel.Ids;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Insights;

public sealed class RequestInsightContextBuilder : IRequestInsightContextBuilder
{
    private readonly ILicenseExtensionRequestRepository _requests;
    private readonly IApprovalCaseRepository _approvals;
    private readonly ILicenseExtensionSagaRepository _sagas;
    private readonly ILocalFulfillmentOutcomeStore _ledger;
    private readonly IAuditEventStore _audit;
    private readonly IOutboxInsightStore _outbox;
    private readonly IReadOnlyDictionary<ProviderSystem, IProviderOperationStatusClient> _providerClients;
    private readonly ILogger<RequestInsightContextBuilder> _logger;

    public RequestInsightContextBuilder(
        ILicenseExtensionRequestRepository requests,
        IApprovalCaseRepository approvals,
        ILicenseExtensionSagaRepository sagas,
        ILocalFulfillmentOutcomeStore ledger,
        IAuditEventStore audit,
        IOutboxInsightStore outbox,
        IEnumerable<IProviderOperationStatusClient> providerClients,
        ILogger<RequestInsightContextBuilder> logger)
    {
        _requests = requests;
        _approvals = approvals;
        _sagas = sagas;
        _ledger = ledger;
        _audit = audit;
        _outbox = outbox;
        _providerClients = providerClients.ToDictionary(c => c.Provider);
        _logger = logger;
    }

    public async Task<RequestInsightContext> BuildAsync(
        RequestId requestId,
        bool includeLiveProbes,
        CancellationToken cancellationToken = default)
    {
        var request = await _requests.GetByIdAsync(requestId, cancellationToken)
            ?? throw new NotFoundException(
                $"License-extension request '{requestId.Value}' was not found.");

        var approval = await _approvals.GetByRequestIdAsync(requestId, cancellationToken);
        var saga = await _sagas.GetByRequestIdReadOnlyAsync(requestId, cancellationToken);
        var ledgerRows = await _ledger.ListByRequestAsync(requestId, cancellationToken);
        var ledgerByLogical = ledgerRows
            .GroupBy(r => r.LogicalOperationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Succeeded).ThenByDescending(x => x.RecordedAtUtc).First());

        var poison = await _outbox.ListPoisonByRequestAsync(requestId, 25, cancellationToken);
        var audit = await _audit.ListByRequestKeysetAsync(requestId, 20, null, null, cancellationToken);

        var notes = new List<string>();
        var items = new List<InsightItemSnapshot>();

        if (saga is null)
        {
            notes.Add("No saga aggregate exists for this request yet.");
            foreach (var item in request.Items)
            {
                ledgerByLogical.TryGetValue(item.LogicalOperationId.Value, out var ledger);
                items.Add(new InsightItemSnapshot(
                    item.Id.Value,
                    item.EntitlementId.Value,
                    item.LogicalOperationId.Value,
                    item.Provider.ToString(),
                    SagaStatus: null,
                    ledger,
                    Probe: null));
            }
        }
        else
        {
            var providerByLogical = request.Items
                .ToDictionary(i => i.LogicalOperationId.Value, i => i.Provider);

            foreach (var outcome in saga.ItemOutcomes)
            {
                providerByLogical.TryGetValue(outcome.LogicalOperationId.Value, out var provider);
                ledgerByLogical.TryGetValue(outcome.LogicalOperationId.Value, out var ledger);

                ProviderOperationStatus? probe = null;
                if (includeLiveProbes
                    && outcome.Status == SagaItemOutcomeStatus.Uncertain
                    && provider != default
                    && _providerClients.TryGetValue(provider, out var client))
                {
                    try
                    {
                        probe = await client.GetByLogicalOperationIdAsync(
                            outcome.LogicalOperationId,
                            correlationId: null,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Insight probe failed for LogicalOperationId {LogicalOperationId}",
                            outcome.LogicalOperationId.Value);
                        notes.Add(
                            $"Probe failed for {outcome.LogicalOperationId.Value:N}: {ex.Message}");
                        probe = new ProviderOperationStatus(
                            provider,
                            outcome.LogicalOperationId,
                            ProviderOperationLookupKind.Unknown,
                            ProviderStatus: null,
                            NewExpiryUtc: null,
                            FailureReason: null,
                            Detail: ex.Message);
                    }
                }

                items.Add(new InsightItemSnapshot(
                    outcome.RequestItemId.Value,
                    outcome.EntitlementId.Value,
                    outcome.LogicalOperationId.Value,
                    provider == default ? null : provider.ToString(),
                    outcome.Status,
                    ledger,
                    probe));
            }
        }

        return new RequestInsightContext
        {
            RequestId = requestId.Value,
            Request = request,
            Approval = approval,
            Saga = saga,
            Items = items,
            PoisonOutbox = poison,
            RecentAudit = audit,
            BuilderNotes = notes
        };
    }
}
