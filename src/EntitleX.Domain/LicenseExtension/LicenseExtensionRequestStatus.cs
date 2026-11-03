namespace EntitleX.Domain.LicenseExtension;

/// <summary>Lifecycle status for a license-extension request.</summary>
public enum LicenseExtensionRequestStatus
{
    Draft = 0,
    Submitted = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    InFulfillment = 5,
    PartiallyFulfilled = 6,
    Fulfilled = 7,
    Failed = 8,
    Cancelled = 9
}
