using EntitleX.Application.Insights.Abstractions;
using Microsoft.Extensions.Options;

namespace EntitleX.Infrastructure.Insights;

public sealed class OptionsAiAssistGate(IOptionsMonitor<AiAssistOptions> options) : IAiAssistGate
{
    public bool IsEnabled
    {
        get
        {
            var value = options.CurrentValue;
            return value.Enabled
                && !string.IsNullOrWhiteSpace(value.BaseUrl)
                && !string.IsNullOrWhiteSpace(value.Model);
        }
    }

    public string? Model => IsEnabled ? options.CurrentValue.Model : null;

    public float Temperature
    {
        get
        {
            var temperature = options.CurrentValue.Temperature;
            return temperature < 0 ? 0 : temperature;
        }
    }
}
