using EntitleX.Application.Reconciliation.Models;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Idempotent GET of provider AppliedLicenseOperation status by LogicalOperationId.
/// Never submits extensions — read-only reconciliation probe.
/// </summary>
public interface IProviderOperationStatusClient
{
    ProviderSystem Provider { get; }

    Task<ProviderOperationStatus> GetByLogicalOperationIdAsync(
        LogicalOperationId logicalOperationId,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
