namespace EntitleX.Application.Fulfillment;

/// <summary>
/// Application-owned fulfillment enqueue settings. Bound from Messaging:Sqs in hosts
/// so Application never references Messaging adapter options.
/// </summary>
public sealed class FulfillmentEnqueueOptions
{
    public const string SectionName = "FulfillmentEnqueue";

    public string FulfillmentQueueName { get; set; } = "entitlex-fulfillment";
}
