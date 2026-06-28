using Microsoft.Extensions.DependencyInjection;

namespace Modulus.Inbox.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Events.Abstractions;
using Modulus.Inbox.Abstractions;

public static class InboxServiceCollectionExtensions
{
    /// <summary>
    /// Wraps all <c>IIntegrationEventHandler{T}</c> registrations with the
    /// idempotent decorator (<see cref="IdempotentIntegrationEventHandler{TEvent}"/>),
    /// registers <see cref="EfInboxStore"/> as <see cref="IInboxStore"/>,
    /// and configures inbox options.
    /// </summary>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null)
        where TContext : DbContext
    {
        services.AddOptions<InboxOptions>()
            .Configure(opts => configure?.Invoke(opts));

        // Register TContext as DbContext for EfInboxStore to resolve.
        services.AddScoped<DbContext>(
            sp => sp.GetRequiredService<TContext>());

        services.AddScoped<IInboxStore, EfInboxStore>();

        return services.DecorateIntegrationEventHandlers();
    }

    /// <summary>
    /// Replaces every <c>IIntegrationEventHandler{T}</c> registration with a
    /// decorator that wraps it in <see cref="IdempotentIntegrationEventHandler{TEvent}"/>.
    /// Called by <c>AddInbox</c> and <c>AddMongoInbox</c> after registering the
    /// appropriate <see cref="IInboxStore"/> implementation.
    /// </summary>
    public static IServiceCollection DecorateIntegrationEventHandlers(
        this IServiceCollection services)
    {
        var handlerDescriptors = services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition()
                   == typeof(IIntegrationEventHandler<>))
            .ToList();

        foreach (var descriptor in handlerDescriptors)
        {
            var eventType = descriptor.ServiceType.GetGenericArguments()[0];
            var decoratorType = typeof(IdempotentIntegrationEventHandler<>)
                .MakeGenericType(eventType);

            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Scoped(
                descriptor.ServiceType,
                sp =>
                {
                    object inner = descriptor.ImplementationType is not null
                        ? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType)
                        : descriptor.ImplementationFactory!(sp);

                    return ActivatorUtilities.CreateInstance(sp, decoratorType, inner);
                }));
        }

        return services;
    }
}
