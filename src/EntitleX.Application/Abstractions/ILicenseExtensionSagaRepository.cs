using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.Abstractions;

public interface ILicenseExtensionSagaRepository
{
    Task AddAsync(LicenseExtensionSaga saga, CancellationToken cancellationToken = default);

    Task<LicenseExtensionSaga?> GetByRequestIdAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Read-only saga load (AsNoTracking) for GET APIs — avoid N+1 and change-tracker cost.</summary>
    Task<LicenseExtensionSaga?> GetByRequestIdReadOnlyAsync(
        RequestId requestId,
        CancellationToken cancellationToken = default);

    Task<LicenseExtensionSaga?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sagas with an elapsed timeout deadline (hook scan for workers/lambdas).
    /// </summary>
    Task<IReadOnlyList<LicenseExtensionSaga>> ListTimedOutAsync(
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sagas needing reconciliation repair (ReconciliationRequired and/or Uncertain items).
    /// </summary>
    Task<IReadOnlyList<LicenseExtensionSaga>> ListRequiringReconciliationAsync(
        int take,
        CancellationToken cancellationToken = default);
}
