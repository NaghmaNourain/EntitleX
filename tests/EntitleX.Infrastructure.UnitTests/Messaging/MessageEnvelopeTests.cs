using EntitleX.Messaging.Contracts;

namespace EntitleX.Infrastructure.UnitTests.Messaging;

public sealed class MessageEnvelopeTests
{
    [Fact]
    public void Create_RoundTripsPayloadAndIdentifiers()
    {
        var payload = new FulfillmentCommandV1(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "EXT-1",
            [
                new FulfillmentItemV1(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "ent-1",
                    "CloudShield",
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    DateTimeOffset.Parse("2026-02-01T00:00:00Z"))
            ]);

        var messageId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var correlationId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var envelope = MessageEnvelope.Create(
            MessageTypes.FulfillmentCommand,
            payload,
            messageId,
            correlationId: correlationId,
            requestId: payload.RequestId);

        Assert.Equal(MessageEnvelope.CurrentSchemaVersion, envelope.SchemaVersion);
        Assert.Equal(MessageTypes.FulfillmentCommand, envelope.MessageType);
        Assert.Equal(messageId, envelope.MessageId);
        Assert.Equal(correlationId, envelope.CorrelationId);
        Assert.Equal(payload.RequestId, envelope.RequestId);

        var roundTrip = envelope.DeserializePayload<FulfillmentCommandV1>();
        Assert.Equal(payload.ExtensionNumber, roundTrip.ExtensionNumber);
        Assert.Single(roundTrip.Items);
        Assert.Equal("ent-1", roundTrip.Items[0].EntitlementId);
    }
}
