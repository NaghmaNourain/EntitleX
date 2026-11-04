namespace EntitleX.Infrastructure.Observability;

/// <summary>Async-local correlation id shared by middleware and outbound HTTP.</summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentValue = new();

    public static string? Current
    {
        get => CurrentValue.Value;
        set => CurrentValue.Value = value;
    }
}
