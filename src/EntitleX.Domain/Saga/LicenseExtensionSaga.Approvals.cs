using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.Common;

namespace EntitleX.Domain.Saga;

public sealed partial class LicenseExtensionSaga
{
    /// <summary>
    /// Records Renewal Manager approval. Enforces ordering: Deal Desk cannot
    /// be skipped when required; Deal Desk cannot run before RM.
    /// </summary>
    public void RecordRenewalManagerApproval(
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (SagaState.PendingRenewalManagerApproval or SagaState.Escalated))
        {
            throw new DomainException(
                $"Renewal Manager approval is not valid from saga state '{State}'.");
        }

        if (State == SagaState.Escalated &&
            !_transitions.Any(t => t.ToState == SagaState.PendingRenewalManagerApproval ||
                                   t.FromState == SagaState.PendingRenewalManagerApproval))
        {
            // Escalated before RM could act — resume into RM path first.
        }

        RenewalManagerApproved = true;

        if (RequiresDealDesk)
        {
            TransitionTo(
                SagaState.PendingDealDeskApproval,
                "Renewal Manager approved; Deal Desk required next",
                actor,
                correlationId,
                now);
            ScheduleTimeout(SagaTimeoutKind.ApprovalPending, now.AddDays(3), now);
        }
        else
        {
            TransitionTo(
                SagaState.Approved,
                "Renewal Manager approved (final)",
                actor,
                correlationId,
                now);
            ClearTimeout(now);
        }
    }

    /// <summary>
    /// Records Deal Desk approval. Requires prior Renewal Manager approval when
    /// the band mandates sequential stages.
    /// </summary>
    public void RecordDealDeskApproval(
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (!RequiresDealDesk)
        {
            throw new DomainException(
                "Deal Desk approval is not part of this saga's approval band.");
        }

        if (!RenewalManagerApproved)
        {
            throw new DomainException(
                "Deal Desk cannot approve before Renewal Manager (approval ordering).");
        }

        if (State is not (SagaState.PendingDealDeskApproval or SagaState.Escalated))
        {
            throw new DomainException(
                $"Deal Desk approval is not valid from saga state '{State}'.");
        }

        DealDeskApproved = true;
        TransitionTo(
            SagaState.Approved,
            "Deal Desk approved (final)",
            actor,
            correlationId,
            now);
        ClearTimeout(now);
    }

    public void RecordRejection(
        string? actor = null,
        string? correlationId = null,
        string? reason = null,
        DateTimeOffset? atUtc = null)
    {
        EnsureNotTerminal();
        var now = atUtc ?? DateTimeOffset.UtcNow;

        if (State is not (
            SagaState.Requested or
            SagaState.PendingRenewalManagerApproval or
            SagaState.PendingDealDeskApproval or
            SagaState.Escalated))
        {
            throw new DomainException(
                $"Rejection is not valid from saga state '{State}'.");
        }

        TransitionTo(
            SagaState.Rejected,
            reason ?? "Request rejected during approval",
            actor,
            correlationId,
            now);
        ClearTimeout(now);
    }
    public void ResumeFromEscalation(
        SagaState target,
        string reason,
        string? actor = null,
        string? correlationId = null,
        DateTimeOffset? atUtc = null)
    {
        if (State != SagaState.Escalated)
        {
            throw new DomainException("ResumeFromEscalation requires Escalated state.");
        }

        var now = atUtc ?? DateTimeOffset.UtcNow;
        TransitionTo(target, reason, actor, correlationId, now);
    }
    private void RouteInitialBand(string? correlationId, DateTimeOffset now)
    {
        switch (ApprovalBand)
        {
            case ApprovalBand.AutoApprove:
                TransitionTo(
                    SagaState.Approved,
                    "Auto-approved by day-band policy",
                    actor: "system",
                    correlationId,
                    now);
                break;

            case ApprovalBand.RenewalManagerOnly:
                TransitionTo(
                    SagaState.PendingRenewalManagerApproval,
                    "Awaiting Renewal Manager approval",
                    actor: "system",
                    correlationId,
                    now);
                ScheduleTimeout(SagaTimeoutKind.ApprovalPending, now.AddDays(3), now);
                break;

            case ApprovalBand.RenewalManagerThenDealDesk:
                TransitionTo(
                    SagaState.PendingRenewalManagerApproval,
                    "Awaiting Renewal Manager then Deal Desk",
                    actor: "system",
                    correlationId,
                    now);
                ScheduleTimeout(SagaTimeoutKind.ApprovalPending, now.AddDays(3), now);
                break;

            case ApprovalBand.Unsupported:
                TransitionTo(
                    SagaState.Rejected,
                    "Extension length unsupported by policy",
                    actor: "system",
                    correlationId,
                    now);
                break;

            default:
                throw new DomainException($"Unknown approval band '{ApprovalBand}'.");
        }
    }
    private bool IsApprovedPathComplete() =>
        ApprovalBand switch
        {
            ApprovalBand.AutoApprove => true,
            ApprovalBand.RenewalManagerOnly => RenewalManagerApproved,
            ApprovalBand.RenewalManagerThenDealDesk => RenewalManagerApproved && DealDeskApproved,
            _ => false
        };

}
