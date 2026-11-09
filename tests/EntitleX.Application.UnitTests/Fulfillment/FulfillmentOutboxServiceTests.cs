using EntitleX.Application.Abstractions;
using EntitleX.Application.Fulfillment;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Messaging.Contracts;
using Microsoft.Extensions.Options;

namespace EntitleX.Application.UnitTests.Fulfillment;

public sealed class FulfillmentOutboxServiceTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task PreferDirectKafka_WritesProviderCommandsToKafkaOutbox()
    {
        var request = CreateApprovedRequest();
        var writer = new CapturingOutboxWriter();
        var options = Options.Create(new FulfillmentEnqueueOptions
        {
            PreferDirectKafka = true,
            CloudShieldCommandsTopic = "cloudshield.license-extension.commands"
        });

        var service = new FulfillmentOutboxService(writer, options);
        var direct = await service.EnqueueAfterFinalApprovalAsync(
            request,
            correlationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        Assert.True(direct);
        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment, request.Status);
        Assert.Single(writer.Enqueued);
        Assert.Equal(MessageTypes.ProviderLicenseExtensionCommand, writer.Enqueued[0].Envelope.MessageType);
        Assert.Equal(OutboxTransport.Kafka, writer.Enqueued[0].Target.Transport);
        Assert.Equal("cloudshield.license-extension.commands", writer.Enqueued[0].Target.Destination);
        Assert.Equal("ent-1", writer.Enqueued[0].Target.PartitionKey);
    }

    [Fact]
    public async Task LegacySqs_WritesFulfillmentCommand()
    {
        var request = CreateApprovedRequest();
        var writer = new CapturingOutboxWriter();
        var options = Options.Create(new FulfillmentEnqueueOptions
        {
            PreferDirectKafka = false,
            FulfillmentQueueName = "entitlex-fulfillment"
        });

        var service = new FulfillmentOutboxService(writer, options);
        var direct = await service.EnqueueAfterFinalApprovalAsync(request);

        Assert.False(direct);
        Assert.Equal(MessageTypes.FulfillmentCommand, writer.Enqueued[0].Envelope.MessageType);
        Assert.Equal(OutboxTransport.Sqs, writer.Enqueued[0].Target.Transport);
    }

    private static LicenseExtensionRequest CreateApprovedRequest()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-FUL-1",
            "rep@entitlex.local",
            [
                (
                    EntitlementId.From("ent-1"),
                    ProviderSystem.CloudShield,
                    Baseline,
                    Baseline.AddDays(14))
            ]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        return request;
    }

    private sealed class CapturingOutboxWriter : IOutboxWriter
    {
        public List<(MessageEnvelope Envelope, OutboxDispatchTarget Target)> Enqueued { get; } = [];

        public Task EnqueueAsync(
            MessageEnvelope envelope,
            OutboxDispatchTarget target,
            CancellationToken cancellationToken = default)
        {
            Enqueued.Add((envelope, target));
            return Task.CompletedTask;
        }
    }
}
