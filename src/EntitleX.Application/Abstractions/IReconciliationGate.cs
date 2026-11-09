using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Phase 7 stub / Phase 8 port: blocks selective retry while outcomes are uncertain
/// and require reconciliation repair.
/// </summary>
public interface IReconciliationGate
{
    /// <summary>
    /// Returns a blocking reason when retry must wait for reconciliation; otherwise null.
    /// </summary>
    Task<string?> GetRetryBlockReasonAsync(
        RequestId sourceRequestId,
        LicenseExtensionSaga saga,
        CancellationToken cancellationToken = default);
}
