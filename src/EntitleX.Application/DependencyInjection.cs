using System.Reflection;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Authorization;
using EntitleX.Application.Common.Behaviors;
using EntitleX.Application.EntitlementAggregation;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.Fulfillment;
using EntitleX.Application.LicenseExtension.Retry;
using EntitleX.Application.Reconciliation;
using EntitleX.Application.Saga;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EntitleX.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IResourceAuthorizationService, ResourceAuthorizationService>();
        services.AddScoped<IEntitlementAggregationService, EntitlementAggregationService>();
        services.AddScoped<IReconciliationGate, ReconciliationGate>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddSagaOrchestration();

        return services;
    }

    /// <summary>
    /// Saga orchestration without MediatR (workers that already register infrastructure).
    /// </summary>
    public static IServiceCollection AddSagaOrchestration(this IServiceCollection services)
    {
        services.AddScoped<IFulfillmentOutboxService, FulfillmentOutboxService>();
        services.AddScoped<ILicenseExtensionSagaOrchestrator, LicenseExtensionSagaOrchestrator>();
        services.AddScoped<ISagaTimeoutHook, LoggingSagaTimeoutHook>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        return services;
    }
}
