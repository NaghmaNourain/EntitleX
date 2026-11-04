using EntitleX.Application.Abstractions;
using EntitleX.Domain.Saga;
using Microsoft.Extensions.Logging;

namespace EntitleX.Application.Saga;

/// <summary>
/// Default timeout hook: applies the domain timeout transition and logs.
/// Phase 9 lambdas can replace or decorate this registration.
/// </summary>
public sealed class LoggingSagaTimeoutHook(ILogger<LoggingSagaTimeoutHook> logger) : ISagaTimeoutHook
{
    public Task HandleAsync(LicenseExtensionSaga saga, CancellationToken cancellationToken = default)
    {
        var before = saga.State;
        saga.ApplyTimeout(DateTimeOffset.UtcNow);
        logger.LogInformation(
            "Applied saga timeout hook for saga {SagaId} request {RequestId}: {Before} → {After} (kind was scheduled)",
            saga.Id,
            saga.RequestId.Value,
            before,
            saga.State);
        return Task.CompletedTask;
    }
}
