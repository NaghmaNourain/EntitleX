namespace EntitleX.Domain.Approval;

/// <summary>Outcome of evaluating extension-day approval rules.</summary>
public enum ApprovalBand
{
    /// <summary>1–30 days: system auto-approves.</summary>
    AutoApprove = 0,

    /// <summary>31–90 days: Renewal Manager must approve.</summary>
    RenewalManagerOnly = 1,

    /// <summary>91–365 days: Renewal Manager then Deal Desk (sequential).</summary>
    RenewalManagerThenDealDesk = 2,

    /// <summary>&gt;365 days: unsupported — request is rejected.</summary>
    Unsupported = 3
}
