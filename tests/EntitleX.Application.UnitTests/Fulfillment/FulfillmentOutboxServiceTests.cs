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
    public async Task EnqueueAfterFinalApproval_WritesSqsOutboxAndBeginsFulfillment()
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

        var writer = new CapturingOutboxWriter();
        var options = Options.Create(new FulfillmentEnqueueOptions
        {
            FulfillmentQueueName = "entitlex-fulfillment"
        });

        var service = new FulfillmentOutboxService(writer, options);
        await service.EnqueueAfterFinalApprovalAsync(request, correlationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        Assert.Equal(LicenseExtensionRequestStatus.InFulfillment, request.Status);
        Assert.NotNull(writer.LastEnvelope);
        Assert.Equal(MessageTypes.FulfillmentCommand, writer.LastEnvelope!.MessageType);
        Assert.Equal(OutboxTransport.Sqs, writer.LastTarget!.Transport);
        Assert.Equal("entitlex-fulfillment", writer.LastTarget.Destination);

        var payload = writer.LastEnvelope.DeserializePayload<FulfillmentCommandV1>();
        Assert.Equal(request.Id.Value, payload.RequestId);
        Assert.Single(payload.Items);
        Assert.Equal("ent-1", payload.Items[0].EntitlementId);
    }

    private sealed class CapturingOutboxWriter : IOutboxWriter
    {
        public MessageEnvelope? LastEnvelope { get; private set; }

        public OutboxDispatchTarget? LastTarget { get; private set; }

        public Task EnqueueAsync(
            MessageEnvelope envelope,
            OutboxDispatchTarget target,
            CancellationToken cancellationToken = default)
        {
            LastEnvelope = envelope;
            LastTarget = target;
            return Task.CompletedTask;
        }
    }
}
