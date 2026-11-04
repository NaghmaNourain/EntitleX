namespace EntitleX.Messaging.Options;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    public SqsOptions Sqs { get; set; } = new();

    public KafkaOptions Kafka { get; set; } = new();

    public OutboxOptions Outbox { get; set; } = new();

    public FulfillmentOptions Fulfillment { get; set; } = new();
}

public sealed class SqsOptions
{
    public string ServiceUrl { get; set; } = "http://localhost:4566";

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";

    public string FulfillmentQueueName { get; set; } = "entitlex-fulfillment";

    public string FulfillmentDlqName { get; set; } = "entitlex-fulfillment-dlq";

    /// <summary>SQS visibility timeout in seconds for in-flight messages.</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 30;

    public int WaitTimeSeconds { get; set; } = 10;

    public int MaxNumberOfMessages { get; set; } = 5;
}

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "entitlex";

    public string CloudShieldCommandsTopic { get; set; } = "cloudshield.license-extension.commands";

    public string NetworkGuardCommandsTopic { get; set; } = "networkguard.license-extension.commands";

    public string ProviderResultsTopic { get; set; } = "entitlex.provider.results";

    public string FulfillmentConsumerGroup { get; set; } = "entitlex-fulfillment";

    public string ProviderResultsConsumerGroup { get; set; } = "entitlex-orchestrator-results";

    public string CloudShieldConsumerGroup { get; set; } = "cloudshield-commands";

    public string NetworkGuardConsumerGroup { get; set; } = "networkguard-commands";
}

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 20;

    public int PollIntervalMilliseconds { get; set; } = 1000;

    public int LockDurationSeconds { get; set; } = 30;

    public int MaxAttempts { get; set; } = 8;

    public int BaseRetryDelaySeconds { get; set; } = 2;

    public int MaxRetryDelaySeconds { get; set; } = 300;
}

public sealed class FulfillmentOptions
{
    public int IdleDelayMilliseconds { get; set; } = 500;
}
