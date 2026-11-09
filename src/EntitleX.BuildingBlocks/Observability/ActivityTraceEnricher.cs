using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace EntitleX.BuildingBlocks.Observability;

/// <summary>
/// Adds W3C <c>TraceId</c> / <c>SpanId</c> from <see cref="Activity.Current"/> when present.
/// Avoids an extra NuGet package while keeping console logs joinable to Jaeger.
/// </summary>
public sealed class ActivityTraceEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
