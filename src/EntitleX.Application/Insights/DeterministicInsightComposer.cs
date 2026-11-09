using System.Text;
using System.Text.Json;
using EntitleX.Application.Insights.Models;
using EntitleX.Application.Reconciliation.Models;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.Insights;

public sealed class DeterministicInsightComposer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed record ComposedInsight(
        string Summary,
        InsightRecommendedAction RecommendedAction,
        IReadOnlyList<InsightCitation> Citations,
        RequestInsightFactsDto Facts,
        string FactsJson);

    public ComposedInsight Compose(RequestInsightContext context, string insightKind)
    {
        var facts = BuildFacts(context);
        var citations = BuildCitations(context, facts);
        var action = ChooseAction(context, facts);
        var summary = insightKind.Equals("Rca", StringComparison.OrdinalIgnoreCase)
            ? BuildRcaSummary(context, facts, action)
            : BuildExplainSummary(context, facts, action);

        return new ComposedInsight(
            summary,
            action,
            citations,
            facts,
            JsonSerializer.Serialize(facts, JsonOptions));
    }

    private static RequestInsightFactsDto BuildFacts(RequestInsightContext context)
    {
        var uncertain = context.Items.Count(i => i.SagaStatus == SagaItemOutcomeStatus.Uncertain);
        var failed = context.Items.Count(i => i.SagaStatus == SagaItemOutcomeStatus.Failed);
        var pending = context.Items.Count(i => i.SagaStatus == SagaItemOutcomeStatus.Pending);
        var succeeded = context.Items.Count(i => i.SagaStatus == SagaItemOutcomeStatus.Succeeded);

        var mismatches = context.Items
            .Where(IsLedgerMismatch)
            .ToList();

        var itemFacts = context.Items.Select(i => new InsightItemFactDto(
            i.RequestItemId,
            i.EntitlementId,
            i.LogicalOperationId,
            i.Provider,
            i.SagaStatus?.ToString() ?? "None",
            i.Ledger?.Succeeded,
            i.Ledger?.ErrorCode)).ToList();

        var probes = context.Items
            .Where(i => i.Probe is not null)
            .Select(i => new InsightProbeFactDto(
                i.LogicalOperationId,
                i.Probe!.Provider.ToString(),
                i.Probe.Kind.ToString(),
                i.Probe.ProviderStatus,
                i.Probe.Detail))
            .ToList();

        var poison = context.PoisonOutbox.Select(p => new InsightPoisonFactDto(
            p.Id,
            p.MessageType,
            p.Destination,
            p.LastError,
            p.AttemptCount,
            p.CreatedAtUtc)).ToList();

        var audit = context.RecentAudit.Select(a => new InsightAuditFactDto(
            a.Id,
            a.EventType,
            a.Detail,
            a.OccurredAtUtc)).ToList();

        var notes = new List<string>(context.BuilderNotes);
        if (mismatches.Count > 0)
        {
            notes.Add($"{mismatches.Count} ledger↔saga mismatch(es) detected.");
        }

        return new RequestInsightFactsDto(
            context.Request?.Status.ToString(),
            context.Request?.ExtensionNumber,
            context.Request?.RequestedBy,
            context.Approval?.Status.ToString(),
            context.Approval?.Band.ToString() ?? context.Saga?.ApprovalBand.ToString(),
            context.Saga?.State.ToString(),
            context.Saga?.SucceededItemCount ?? succeeded,
            context.Saga?.FailedItemCount ?? failed,
            context.Saga?.PendingItemCount ?? pending,
            uncertain,
            poison.Count,
            mismatches.Count,
            itemFacts,
            probes,
            poison,
            audit,
            notes);
    }

    private static bool IsLedgerMismatch(InsightItemSnapshot item)
    {
        if (item.Ledger is null || item.SagaStatus is null)
        {
            return false;
        }

        if (item.Ledger.Succeeded
            && item.SagaStatus is SagaItemOutcomeStatus.Failed
                or SagaItemOutcomeStatus.Uncertain
                or SagaItemOutcomeStatus.Pending)
        {
            return true;
        }

        if (!item.Ledger.Succeeded && item.SagaStatus == SagaItemOutcomeStatus.Succeeded)
        {
            return true;
        }

        return false;
    }

    private static InsightRecommendedAction ChooseAction(
        RequestInsightContext context,
        RequestInsightFactsDto facts)
    {
        if (facts.PoisonOutboxCount > 0)
        {
            return InsightRecommendedAction.InvestigatePoisonOutbox;
        }

        var uncertainItems = context.Items
            .Where(i => i.SagaStatus == SagaItemOutcomeStatus.Uncertain)
            .ToList();

        var appliedWhileNotSucceeded = uncertainItems.Any(i =>
            i.Probe?.Kind == ProviderOperationLookupKind.ConfirmedApplied);

        var holdProbe = uncertainItems.Any(i =>
            i.Probe is null
            || i.Probe.Kind is ProviderOperationLookupKind.NotFound
                or ProviderOperationLookupKind.Unknown);

        if (facts.LedgerMismatchCount > 0 || appliedWhileNotSucceeded)
        {
            return InsightRecommendedAction.ManualReconcile;
        }

        if (uncertainItems.Count > 0
            || context.Saga?.State == SagaState.ReconciliationRequired)
        {
            if (holdProbe || uncertainItems.Count > 0)
            {
                // ConfirmedFailed on uncertain → treat as retry-eligible after recon marks Failed;
                // while still Uncertain with Failed probe, prefer ManualReconcile to settle locally.
                if (uncertainItems.All(i => i.Probe?.Kind == ProviderOperationLookupKind.ConfirmedFailed))
                {
                    return InsightRecommendedAction.ManualReconcile;
                }

                return InsightRecommendedAction.HoldUncertain;
            }
        }

        if (facts.FailedItemCount > 0
            && facts.UncertainItemCount == 0
            && context.Saga?.State is SagaState.Failed
                or SagaState.PartiallyCompleted
                or SagaState.Completed)
        {
            return InsightRecommendedAction.RetryFailedItems;
        }

        if (context.Approval?.Status == ApprovalCaseStatus.Pending
            || context.Request?.Status == LicenseExtensionRequestStatus.PendingApproval
            || context.Saga?.State is SagaState.PendingRenewalManagerApproval
                or SagaState.PendingDealDeskApproval
                or SagaState.Escalated)
        {
            return InsightRecommendedAction.InvestigateApproval;
        }

        return InsightRecommendedAction.None;
    }

    private static IReadOnlyList<InsightCitation> BuildCitations(
        RequestInsightContext context,
        RequestInsightFactsDto facts)
    {
        var list = new List<InsightCitation>
        {
            new("request", context.RequestId.ToString("D"), "LicenseExtensionRequest")
        };

        if (context.Saga is not null)
        {
            list.Add(new("saga", context.Saga.Id.ToString("D"), $"Saga:{context.Saga.State}"));
        }

        if (context.Approval is not null)
        {
            list.Add(new(
                "approval",
                context.Approval.Id.ToString("D"),
                $"Approval:{context.Approval.Status}/{context.Approval.Band}"));
        }

        foreach (var item in context.Items.Where(i =>
                     i.SagaStatus is SagaItemOutcomeStatus.Failed or SagaItemOutcomeStatus.Uncertain
                     || IsLedgerMismatch(i)))
        {
            list.Add(new(
                "logicalOperation",
                item.LogicalOperationId.ToString("D"),
                $"{item.EntitlementId}:{item.SagaStatus}"));
        }

        foreach (var probe in facts.Probes)
        {
            list.Add(new(
                "probe",
                probe.LogicalOperationId.ToString("D"),
                $"{probe.Provider}:{probe.Kind}"));
        }

        foreach (var poison in facts.PoisonOutbox)
        {
            list.Add(new("outbox", poison.OutboxMessageId.ToString("D"), poison.MessageType));
        }

        foreach (var audit in facts.RecentAudit.Take(5))
        {
            list.Add(new("audit", audit.Id.ToString("D"), audit.EventType));
        }

        return list;
    }

    private static string BuildExplainSummary(
        RequestInsightContext context,
        RequestInsightFactsDto facts,
        InsightRecommendedAction action)
    {
        var sb = new StringBuilder();
        sb.Append("Request ").Append(Short(context.RequestId));
        if (!string.IsNullOrWhiteSpace(facts.ExtensionNumber))
        {
            sb.Append(" (").Append(facts.ExtensionNumber).Append(')');
        }

        sb.Append(" is ").Append(facts.RequestStatus ?? "Unknown");
        if (facts.SagaState is not null)
        {
            sb.Append(" with saga state ").Append(facts.SagaState);
        }

        sb.Append(". Item outcomes: ")
            .Append(facts.SucceededItemCount).Append(" succeeded, ")
            .Append(facts.FailedItemCount).Append(" failed, ")
            .Append(facts.PendingItemCount).Append(" pending, ")
            .Append(facts.UncertainItemCount).Append(" uncertain.");

        if (facts.ApprovalStatus is not null)
        {
            sb.Append(" Approval is ").Append(facts.ApprovalStatus);
            if (facts.ApprovalBand is not null)
            {
                sb.Append(" (band ").Append(facts.ApprovalBand).Append(')');
            }

            sb.Append('.');
        }

        AppendSharedFindings(sb, facts, action);
        return sb.ToString();
    }

    private static string BuildRcaSummary(
        RequestInsightContext context,
        RequestInsightFactsDto facts,
        InsightRecommendedAction action)
    {
        var sb = new StringBuilder();
        sb.Append("Reconciliation RCA for request ").Append(Short(context.RequestId)).Append(": ");
        sb.Append("saga=").Append(facts.SagaState ?? "none")
            .Append(", uncertain=").Append(facts.UncertainItemCount)
            .Append(", ledgerMismatches=").Append(facts.LedgerMismatchCount)
            .Append(", poisonOutbox=").Append(facts.PoisonOutboxCount)
            .Append(", liveProbes=").Append(facts.Probes.Count).Append('.');

        if (facts.Probes.Count > 0)
        {
            sb.Append(" Probe results: ");
            sb.Append(string.Join(
                "; ",
                facts.Probes.Select(p =>
                    $"{Short(p.LogicalOperationId)}→{p.Kind}")));
            sb.Append('.');
        }

        AppendSharedFindings(sb, facts, action);
        return sb.ToString();
    }

    private static void AppendSharedFindings(
        StringBuilder sb,
        RequestInsightFactsDto facts,
        InsightRecommendedAction action)
    {
        if (facts.PoisonOutboxCount > 0)
        {
            sb.Append(' ')
                .Append(facts.PoisonOutboxCount)
                .Append(" poison outbox message(s) are tied to this request.");
        }

        if (facts.LedgerMismatchCount > 0)
        {
            sb.Append(' ')
                .Append(facts.LedgerMismatchCount)
                .Append(" local ledger row(s) disagree with saga item status.");
        }

        foreach (var note in facts.Notes.Take(3))
        {
            sb.Append(' ').Append(note);
        }

        sb.Append(" Recommended next step: ")
            .Append(DescribeAction(action))
            .Append(' ')
            .Append('(')
            .Append(action)
            .Append("). No automated writes are performed by this insight.");
    }

    private static string DescribeAction(InsightRecommendedAction action) =>
        action switch
        {
            InsightRecommendedAction.HoldUncertain =>
                "hold selective retry and wait for / run reconciliation probes — do not blind POST",
            InsightRecommendedAction.ManualReconcile =>
                "run PlatformAdmin manual reconciliation to repair local state from provider truth",
            InsightRecommendedAction.RetryFailedItems =>
                "use selective retry for definitive Failed items only",
            InsightRecommendedAction.InvestigatePoisonOutbox =>
                "inspect and replay poison outbox rows for this request",
            InsightRecommendedAction.InvestigateApproval =>
                "review the pending / escalated approval case",
            _ => "no operator action required from current facts"
        };

    private static string Short(Guid id) => id.ToString("D")[..8] + "…";
}
