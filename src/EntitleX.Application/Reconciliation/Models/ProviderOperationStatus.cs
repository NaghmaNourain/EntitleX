using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Reconciliation.Models;

/// <summary>
/// Normalized provider lookup for an AppliedLicenseOperation by LogicalOperationId.
/// </summary>
public enum ProviderOperationLookupKind
{
    /// <summary>Provider confirmed Applied / success.</summary>
    ConfirmedApplied = 1,

    /// <summary>Provider recorded a definitive failure for this logical op.</summary>
    ConfirmedFailed = 2,

    /// <summary>Provider returned 404 / no operation record.</summary>
    NotFound = 3,

    /// <summary>Transport/error or ambiguous response — do not blind-retry.</summary>
    Unknown = 4
}

public sealed record ProviderOperationStatus(
    ProviderSystem Provider,
    LogicalOperationId LogicalOperationId,
    ProviderOperationLookupKind Kind,
    string? ProviderStatus,
    DateTimeOffset? NewExpiryUtc,
    string? FailureReason,
    string? Detail);
