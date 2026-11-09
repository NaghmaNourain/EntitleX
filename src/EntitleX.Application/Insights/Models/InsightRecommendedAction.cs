namespace EntitleX.Application.Insights.Models;

/// <summary>Operator next step suggested from grounded control-plane facts (never auto-executed).</summary>
public enum InsightRecommendedAction
{
    None = 0,
    HoldUncertain = 1,
    ManualReconcile = 2,
    RetryFailedItems = 3,
    InvestigatePoisonOutbox = 4,
    InvestigateApproval = 5
}
