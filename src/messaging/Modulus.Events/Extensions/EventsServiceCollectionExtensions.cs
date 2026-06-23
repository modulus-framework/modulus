namespace Modulus.Events.Extensions;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;

public static class EventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the domain-event dispatcher, the default in-process
    /// <see cref="IModuleBus"/>, the integration-event registry, and all
    /// handler implementations found in <paramref name="assemblies"/>.
    /// </summary>
    public static IServiceCollection AddModulusEvents(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<IntegrationEventDispatcher>();

        // Registry (singleton — populated once at startup)
        var registry = new IntegrationEventRegistry();
        services.AddSingleton<IIntegrationEventRegistry>(registry);

        foreach (var assembly in assemblies)
        {
            RegisterHandlers(services, assembly);
            RegisterEventTypes(registry, assembly);
        }

        // Default bus: in-process.  Call AddRabbitMqEventBus / AddKafkaEventBus
        // AFTER this to swap the implementation.
        services.AddScoped<IModuleBus, InProcessModuleBus>();

        return services;
    }

    private static void RegisterHandlers(
        IServiceCollection services, Assembly assembly)
    {
        var handlerInterfaces = new[]
        {
            typeof(IDomainEventHandler<>),
            typeof(IIntegrationEventHandler<>),
        };

        foreach (var type in assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var iface in type.GetInterfaces()
                .Where(i => i.IsGenericType
                    && handlerInterfaces.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(iface, type);
            }
        }
    }

    /// <summary>
    /// Registers every concrete <see cref="IIntegrationEvent"/> found in the
    /// assembly so broker consumers know which routing keys to subscribe to.
    /// </summary>
    private static void RegisterEventTypes(
        IIntegrationEventRegistry registry, Assembly assembly)
    {
        var eventType = typeof(IIntegrationEvent);

        foreach (var type in assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                     && eventType.IsAssignableFrom(t)))
        {
            registry.Register(type);
        }
    }
}
