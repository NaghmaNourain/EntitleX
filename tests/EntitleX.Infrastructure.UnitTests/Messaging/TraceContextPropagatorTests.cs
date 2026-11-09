using System.Diagnostics;
using System.Text;
using Amazon.SQS.Model;
using Confluent.Kafka;
using EntitleX.BuildingBlocks.Observability;
using EntitleX.Messaging.Metrics;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace EntitleX.Infrastructure.UnitTests.Messaging;

public sealed class TraceContextPropagatorTests : IDisposable
{
    private readonly ActivityListener _listener;

    public TraceContextPropagatorTests()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void TryParse_ValidTraceParent_Succeeds()
    {
        using var parent = EntitleXActivitySources.Messaging.StartActivity(
            "test-parent",
            ActivityKind.Producer);
        Assert.NotNull(parent);
        Assert.False(string.IsNullOrWhiteSpace(parent!.Id));

        var ok = TraceContextPropagator.TryParse(parent.Id, parent.TraceStateString, out var ctx);

        Assert.True(ok);
        Assert.Equal(parent.TraceId, ctx.TraceId);
    }

    [Fact]
    public void StartActivity_WithExtractedParent_SharesTraceId()
    {
        using var parent = EntitleXActivitySources.Messaging.StartActivity(
            "test-parent",
            ActivityKind.Producer);
        Assert.NotNull(parent);

        Assert.True(TraceContextPropagator.TryParse(parent!.Id, null, out var parentCtx));
        using var child = EntitleXActivitySources.Messaging.StartActivity(
            "messaging.consume kafka",
            ActivityKind.Consumer,
            parentCtx);

        Assert.NotNull(child);
        Assert.Equal(parent.TraceId, child!.TraceId);
        Assert.NotEqual(parent.SpanId, child.SpanId);
    }

    [Fact]
    public void TryParseFromKafkaHeaders_ReadsTraceParent()
    {
        using var parent = EntitleXActivitySources.Messaging.StartActivity(
            "kafka-parent",
            ActivityKind.Producer);
        Assert.NotNull(parent);

        var headers = new Headers
        {
            { "traceparent", Encoding.UTF8.GetBytes(parent!.Id!) }
        };

        var ok = TraceContextPropagator.TryParseFromKafkaHeaders(headers, out var ctx);

        Assert.True(ok);
        Assert.Equal(parent.TraceId, ctx.TraceId);
    }

    [Fact]
    public void TryParseFromSqsMessageAttributes_ReadsTraceParent()
    {
        using var parent = EntitleXActivitySources.Messaging.StartActivity(
            "sqs-parent",
            ActivityKind.Producer);
        Assert.NotNull(parent);

        var attrs = new Dictionary<string, MessageAttributeValue>
        {
            ["traceparent"] = new() { DataType = "String", StringValue = parent!.Id }
        };

        var ok = TraceContextPropagator.TryParseFromSqsMessageAttributes(attrs, out var ctx);

        Assert.True(ok);
        Assert.Equal(parent.TraceId, ctx.TraceId);
    }

    [Fact]
    public void ActivityTraceEnricher_AddsTraceAndSpan()
    {
        using var activity = EntitleXActivitySources.Messaging.StartActivity(
            "enricher-test",
            ActivityKind.Internal);
        Assert.NotNull(activity);

        var enricher = new ActivityTraceEnricher();
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate: new MessageTemplateParser().Parse("hi"),
            properties: Array.Empty<LogEventProperty>());

        enricher.Enrich(logEvent, new SimplePropertyFactory());

        Assert.True(logEvent.Properties.ContainsKey("TraceId"));
        Assert.True(logEvent.Properties.ContainsKey("SpanId"));
    }

    private sealed class SimplePropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
