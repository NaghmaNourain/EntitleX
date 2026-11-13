namespace EntitleX.Application.Insights.Abstractions;

/// <summary>
/// Read-only view of AiAssist configuration for Application-layer consumers.
/// When disabled, structured diagnosis is unavailable and Insights use Fallback only.
/// </summary>
public interface IAiAssistGate
{
    bool IsEnabled { get; }

    string? Model { get; }

    float Temperature { get; }
}
