using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Inbox.Extensions;

using Microsoft.EntityFrameworkCore;
using Modulus.Events.Abstractions;

public static class InboxServiceCollectionExtensions
{
    /// <summary>
    /// Wraps all IIntegrationEventHandler{T} registrations with the
    /// idempotent decorator automatically.
    /// </summary>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        // Register TContext as DbContext for the decorator to use
        services.AddScoped<DbContext>(
            sp => sp.GetRequiredService<TContext>());

        // Decorate all IIntegrationEventHandler<T> registrations
        // Find all registered handler types and wrap them
        var handlerDescriptors = services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition()
                   == typeof(IIntegrationEventHandler<>))
            .ToList();

        foreach (var descriptor in handlerDescriptors)
        {
            var eventType      = descriptor.ServiceType.GetGenericArguments()[0];
            var decoratorType  = typeof(IdempotentIntegrationEventHandler<>)
                .MakeGenericType(eventType);

            // Re-register the handler wrapped in the decorator
            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Scoped(
                descriptor.ServiceType,
                sp =>
                {
                    var inner = descriptor.ImplementationType is not null
                        ? (dynamic)sp.GetRequiredService(descriptor.ImplementationType)
                        : (dynamic)descriptor.ImplementationFactory!(sp);
                    return ActivatorUtilities.CreateInstance(sp, decoratorType, inner);
                }));
        }

        return services;
    }
}