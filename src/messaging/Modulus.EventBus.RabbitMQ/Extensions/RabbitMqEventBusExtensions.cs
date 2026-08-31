using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.EventBus.RabbitMQ;
using Modulus.Events.Abstractions;
using Modulus.Events.Extensions;

namespace Modulus.EventBus.RabbitMQ.Extensions;

public static class RabbitMqEventBusExtensions
{
    /// <summary>
    /// Replaces the default in-process <see cref="IModuleBus"/> with a
    /// RabbitMQ implementation.  Must be called AFTER
    /// <see cref="EventsServiceCollectionExtensions.AddModulusEvents"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional inline configuration callback.</param>
    /// <param name="sectionName">Configuration section name (default: <c>"EventBus:RabbitMq"</c>).</param>
    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null,
        string sectionName = "EventBus:RabbitMq")
    {
        services.AddOptions<RabbitMqOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
                cfg.GetSection(sectionName).Bind(opts))
            .Configure(opts => configure?.Invoke(opts));

        services.RemoveIModuleBusRegistrations();
        services.AddSingleton<RabbitMqEventBus>();
        services.AddScoped<IModuleBus>(sp => sp.GetRequiredService<RabbitMqEventBus>());

        services.AddHostedService<RabbitMqEventConsumer>();

        // Register health check for broker connectivity (TryAddEnumerable because a
        // multi-bus app might not use RabbitMQ, or a test might swap it out; keying on
        // implementation type rather than interface allows multiple bus implementations).
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IModuleHealthCheck, RabbitMqHealthCheck>());

        return services;
    }
}
