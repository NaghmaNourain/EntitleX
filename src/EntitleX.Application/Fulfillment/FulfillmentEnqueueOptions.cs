namespace EntitleX.Application.Fulfillment;

/// <summary>
/// Application-owned fulfillment enqueue settings. Bound from Messaging:* in hosts
/// so Application never references Messaging adapter options.
/// </summary>
public sealed class FulfillmentEnqueueOptions
{
    public const string SectionName = "FulfillmentEnqueue";

    /// <summary>
    /// When true, enqueue provider commands directly to Kafka via the outbox
    /// (preferred). When false, enqueue a single SQS FulfillmentCommand (legacy hop).
    /// </summary>
    public bool PreferDirectKafka { get; set; } = true;

    public string FulfillmentQueueName { get; set; } = "entitlex-fulfillment";

    public string CloudShieldCommandsTopic { get; set; } = "cloudshield.license-extension.commands";

    public string NetworkGuardCommandsTopic { get; set; } = "networkguard.license-extension.commands";
}
