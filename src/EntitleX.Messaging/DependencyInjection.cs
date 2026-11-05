using EntitleX.Messaging.Abstractions;
using EntitleX.Messaging.Kafka;
using EntitleX.Messaging.Metrics;
using EntitleX.Messaging.Options;
using EntitleX.Messaging.Sqs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntitleX.Messaging;

public static class DependencyInjection
{
    public static IServiceCollection AddEntitleXMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));
        services.AddSingleton<IOutboxMetrics, OpenTelemetryOutboxMetrics>();
        services.AddSingleton<SqsMessagePublisher>();
        services.AddSingleton<ISqsMessagePublisher>(sp => sp.GetRequiredService<SqsMessagePublisher>());
        services.AddSingleton<SqsFulfillmentConsumer>();
        services.AddSingleton<SqsDepthProbe>();
        services.AddSingleton<KafkaMessagePublisher>();
        services.AddSingleton<IKafkaMessagePublisher>(sp => sp.GetRequiredService<KafkaMessagePublisher>());
        return services;
    }
}
