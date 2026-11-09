using System.Diagnostics;
using System.Text;
using Amazon.SQS.Model;
using Confluent.Kafka;

namespace EntitleX.Messaging.Metrics;

/// <summary>Parse W3C traceparent/tracestate carried on SQS attributes / Kafka headers.</summary>
public static class TraceContextPropagator
{
    public const string TraceParentKey = "traceparent";
    public const string TraceStateKey = "tracestate";

    public static bool TryParse(
        string? traceParent,
        string? traceState,
        out ActivityContext context)
    {
        context = default;
        if (string.IsNullOrWhiteSpace(traceParent))
        {
            return false;
        }

        return ActivityContext.TryParse(traceParent.Trim(), traceState, out context);
    }

    public static bool TryParseFromKafkaHeaders(Headers? headers, out ActivityContext context)
    {
        context = default;
        if (headers is null)
        {
            return false;
        }

        string? traceParent = null;
        string? traceState = null;
        foreach (var header in headers)
        {
            if (header.Key.Equals(TraceParentKey, StringComparison.OrdinalIgnoreCase)
                && header.GetValueBytes() is { Length: > 0 } tp)
            {
                traceParent = Encoding.UTF8.GetString(tp);
            }
            else if (header.Key.Equals(TraceStateKey, StringComparison.OrdinalIgnoreCase)
                     && header.GetValueBytes() is { Length: > 0 } ts)
            {
                traceState = Encoding.UTF8.GetString(ts);
            }
        }

        return TryParse(traceParent, traceState, out context);
    }

    public static bool TryParseFromSqsMessageAttributes(
        Dictionary<string, MessageAttributeValue>? attributes,
        out ActivityContext context)
    {
        context = default;
        if (attributes is null || attributes.Count == 0)
        {
            return false;
        }

        string? traceParent = null;
        string? traceState = null;
        if (attributes.TryGetValue(TraceParentKey, out var tpAttr)
            && !string.IsNullOrWhiteSpace(tpAttr.StringValue))
        {
            traceParent = tpAttr.StringValue;
        }

        if (attributes.TryGetValue(TraceStateKey, out var tsAttr)
            && !string.IsNullOrWhiteSpace(tsAttr.StringValue))
        {
            traceState = tsAttr.StringValue;
        }

        return TryParse(traceParent, traceState, out context);
    }
}
