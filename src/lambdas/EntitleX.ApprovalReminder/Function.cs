using EntitleX.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EntitleX.ApprovalReminder;

/// <summary>
/// Approval-reminder scheduled function. Local host runner via <c>dotnet run</c>;
/// AWS Lambda / EventBridge invoke <see cref="HandlerAsync"/>.
/// </summary>
public sealed class Function
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Function> _logger;

    public Function(IServiceScopeFactory scopeFactory, ILogger<Function> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ApprovalReminderRunResult> HandlerAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IApprovalReminderService>();
        var result = await service.RunAsync(cancellationToken: cancellationToken);

        _logger.LogInformation(
            "ApprovalReminder complete: scanned={Scanned} reminders={Reminders} escalations={Escalations} skipped={Skipped}",
            result.CandidatesScanned,
            result.RemindersSent,
            result.EscalationsApplied,
            result.SkippedIdempotent);

        return result;
    }
}
