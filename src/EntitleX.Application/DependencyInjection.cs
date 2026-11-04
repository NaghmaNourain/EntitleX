using System.Reflection;
using EntitleX.Application.Abstractions;
using EntitleX.Application.Common.Behaviors;
using EntitleX.Application.EntitlementAggregation;
using EntitleX.Application.EntitlementAggregation.Abstractions;
using EntitleX.Application.Fulfillment;
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

        services.AddScoped<IEntitlementAggregationService, EntitlementAggregationService>();
        services.AddScoped<IFulfillmentOutboxService, FulfillmentOutboxService>();

        return services;
    }
}
