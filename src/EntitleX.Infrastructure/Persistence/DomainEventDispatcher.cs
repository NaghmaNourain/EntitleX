using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension.Events;
using MediatR;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntitleX.Infrastructure.Persistence;

/// <summary>MediatR wrapper so Domain stays free of MediatR.</summary>
public sealed record DomainEventNotification(object DomainEvent) : INotification;

/// <summary>
/// Dispatches aggregate domain events after a successful SaveChanges.
/// Events are cleared from aggregates before publish to avoid re-entrancy loops.
/// </summary>
public sealed class DomainEventSaveChangesInterceptor(
    IServiceProvider serviceProvider,
    ILogger<DomainEventSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null || result <= 0)
        {
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        var aggregates = eventData.Context.ChangeTracker
            .Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        if (aggregates.Count == 0)
        {
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetService<IPublisher>();
        if (publisher is null)
        {
            logger.LogDebug(
                "Skipping {Count} domain event(s): MediatR IPublisher is not registered in this host",
                events.Count);
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        foreach (var domainEvent in events)
        {
            try
            {
                await publisher.Publish(new DomainEventNotification(domainEvent), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Domain event publish failed for {EventType}",
                    domainEvent.GetType().Name);
                throw;
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}

public sealed class DomainEventNotificationHandler(
    ILogger<DomainEventNotificationHandler> logger)
    : INotificationHandler<DomainEventNotification>
{
    public Task Handle(DomainEventNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.DomainEvent)
        {
            case LicenseExtensionRequestCreatedDomainEvent created:
                logger.LogInformation(
                    "LicenseExtensionRequest created {RequestId} ({ExtensionNumber}) at {OccurredAtUtc}",
                    created.RequestId.Value,
                    created.ExtensionNumber,
                    created.OccurredAtUtc);
                break;
            default:
                logger.LogDebug(
                    "Unhandled domain event {EventType}",
                    notification.DomainEvent.GetType().FullName);
                break;
        }

        return Task.CompletedTask;
    }
}

public static class DomainEventDispatchExtensions
{
    public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services)
    {
        services.AddScoped<DomainEventSaveChangesInterceptor>();
        services.AddTransient<INotificationHandler<DomainEventNotification>, DomainEventNotificationHandler>();
        return services;
    }
}
