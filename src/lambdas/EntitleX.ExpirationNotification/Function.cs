namespace EntitleX.ExpirationNotification;

/// <summary>
/// Phase 1 stub for the license-expiration notification function.
/// Wire to AWS Lambda / EventBridge in a later phase.
/// </summary>
public sealed class Function
{
    public Task HandlerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] ExpirationNotification stub invoked");
        return Task.CompletedTask;
    }
}
