using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.Inbox.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.EntityFrameworkCore.ModelBuilding;
using Modulus.Events.Abstractions;
using Modulus.Inbox.Abstractions;

public static class InboxServiceCollectionExtensions
{
    /// <summary>
    /// Wraps all <c>IIntegrationEventHandler{T}</c> registrations with the
    /// idempotent decorator (<see cref="IdempotentIntegrationEventHandler{TEvent}"/>),
    /// registers <see cref="EfInboxStore"/> as <see cref="IInboxStore"/>, maps
    /// the <see cref="InboxMessage"/> entity into every module context, and
    /// configures inbox options.
    /// </summary>
    /// <remarks>
    /// The store is bound to the <b>named</b> <typeparamref name="TContext"/>
    /// via a factory closure. It must NOT resolve a bare
    /// <see cref="DbContext"/>: with several module contexts registered, that
    /// resolves to whichever context was registered LAST (module databases,
    /// outbox, …), silently relocating the inbox table whenever registrations
    /// change. In multi-module apps the inbox lives in the context named by
    /// the LAST <c>AddInbox&lt;TContext&gt;</c> call (rows are mapped into
    /// every module context by the model contributor).
    /// </remarks>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null)
        where TContext : DbContext
    {
        services.AddOptions<InboxOptions>()
            .Configure(opts => configure?.Invoke(opts));

        services.AddScoped<IInboxStore>(
            sp => new EfInboxStore(sp.GetRequiredService<TContext>()));

        // Map InboxMessage into every ModuleDbContext (TryAddEnumerable so a
        // second AddInbox call — e.g. for another context — is idempotent).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IModuleModelContributor, InboxModelContributor>());

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
