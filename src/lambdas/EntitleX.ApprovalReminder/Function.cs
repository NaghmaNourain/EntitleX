namespace EntitleX.ApprovalReminder;

/// <summary>
/// Phase 1 stub for the approval-reminder scheduled function.
/// Wire to AWS Lambda / EventBridge in a later phase.
/// </summary>
public sealed class Function
{
    public Task HandlerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] ApprovalReminder stub invoked");
        return Task.CompletedTask;
    }
}
