using EntitleX.Domain.Saga;

namespace EntitleX.Application.Abstractions;

/// <summary>
/// Timeout hook port. Approval-reminder / reconciliation workers invoke
/// <see cref="HandleAsync"/>; Phase 6 provides a durable-state apply path
/// without requiring the Lambda schedule to exist yet.
/// </summary>
public interface ISagaTimeoutHook
{
    Task HandleAsync(LicenseExtensionSaga saga, CancellationToken cancellationToken = default);
}
