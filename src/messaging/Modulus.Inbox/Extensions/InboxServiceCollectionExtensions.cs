using System.Reflection;
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
    /// Registers inbox deduplication for handlers in the supplied assemblies.
    /// The handler assemblies let a modular host route each handler to its
    /// owning module DbContext when several modules call <c>AddInbox</c>.
    /// </summary>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services,
        params Assembly[] handlerAssemblies)
        where TContext : DbContext
        => AddInboxCore<TContext>(services, configure: null, handlerAssemblies, Type.EmptyTypes);

    /// <summary>
    /// Registers inbox deduplication with options and handler assembly ownership.
    /// </summary>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services,
        Action<InboxOptions> configure,
        params Assembly[] handlerAssemblies)
        where TContext : DbContext
        => AddInboxCore<TContext>(services, configure, handlerAssemblies, Type.EmptyTypes);

    /// <summary>
    /// Registers inbox deduplication for explicitly listed handler implementations.
    /// Type-level routing is useful when handler assemblies cannot distinguish
    /// owners (for example, several test handlers compiled into one assembly).
    /// </summary>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services,
        IEnumerable<Type> handlerImplementationTypes)
        where TContext : DbContext
        => AddInboxCore<TContext>(services, configure: null, Array.Empty<Assembly>(), handlerImplementationTypes.ToArray());

    /// <summary>
    /// Registers inbox deduplication with options and explicit handler ownership.
    /// </summary>
    public static IServiceCollection AddInbox<TContext>(
        this IServiceCollection services,
        Action<InboxOptions> configure,
        IEnumerable<Type> handlerImplementationTypes)
        where TContext : DbContext
        => AddInboxCore<TContext>(services, configure, Array.Empty<Assembly>(), handlerImplementationTypes.ToArray());

    private static IServiceCollection AddInboxCore<TContext>(
        IServiceCollection services,
        Action<InboxOptions>? configure,
        IReadOnlyCollection<Assembly> handlerAssemblies,
        IReadOnlyCollection<Type> handlerImplementationTypes)
        where TContext : DbContext
    {
        services.AddOptions<InboxOptions>()
            .Configure(opts => configure?.Invoke(opts));

        var registry = services.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(InboxStoreRegistry)
                && descriptor.ImplementationInstance is InboxStoreRegistry)
            ?.ImplementationInstance as InboxStoreRegistry;

        if (registry is null)
        {
            registry = new InboxStoreRegistry();
            services.AddSingleton(registry);
        }

        registry.Register<TContext>(handlerAssemblies, handlerImplementationTypes);
        services.AddScoped<IInboxStore>(
            sp => sp.GetRequiredService<InboxStoreRegistry>().CreateDefault(sp));

        // Map InboxMessage into every ModuleDbContext (TryAddEnumerable so a
        // second AddInbox call is idempotent).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IModuleModelContributor, InboxModelContributor>());

        return services.DecorateIntegrationEventHandlers();
    }

    /// <summary>
    /// Registers the inbox's dispatch-time handler decorator. Safe to call any
    /// number of times because the decorator registration is idempotent.
    /// </summary>
    public static IServiceCollection DecorateIntegrationEventHandlers(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IIntegrationEventHandlerDecorator, InboxHandlerDecorator>();
        return services;
    }
}

internal sealed class InboxStoreRegistry
{
    private readonly object _gate = new();
    private readonly List<Registration> _registrations = [];

    public void Register<TContext>(
        IReadOnlyCollection<Assembly> handlerAssemblies,
        IReadOnlyCollection<Type> handlerImplementationTypes)
        where TContext : DbContext
    {
        var assemblies = handlerAssemblies
            .Where(assembly => assembly is not null)
            .Distinct()
            .ToArray();
        var handlerTypes = handlerImplementationTypes
            .Where(handlerType => handlerType is not null)
            .Distinct()
            .ToArray();

        lock (_gate)
        {
            _registrations.RemoveAll(registration =>
                registration.ContextType == typeof(TContext));
            _registrations.Add(new Registration(typeof(TContext), assemblies, handlerTypes));
        }
    }

    public IInboxStore CreateDefault(IServiceProvider services)
    {
        Registration registration;
        lock (_gate)
        {
            if (_registrations.Count != 1)
            {
                throw new InvalidOperationException(
                    "Cannot resolve a default Inbox store because multiple module contexts are registered. " +
                    "Pass the handler assembly to AddInbox<TContext>() so the handler can be routed to its owner.");
            }

            registration = _registrations[0];
        }

        return CreateStore(services, registration);
    }

    public IInboxStore Resolve(IServiceProvider services, Type handlerType)
    {
        Registration? registration;
        lock (_gate)
        {
            registration = _registrations.FirstOrDefault(candidate =>
                candidate.HandlerTypes.Contains(handlerType));

            registration ??= _registrations.FirstOrDefault(candidate =>
                candidate.HandlerAssemblies.Contains(handlerType.Assembly));

            registration ??= _registrations.Count == 1
                && _registrations[0].HandlerAssemblies.Length == 0
                && _registrations[0].HandlerTypes.Length == 0
                ? _registrations[0]
                : null;
        }

        if (registration is null)
        {
            var available = string.Join(", ",
                _registrations.SelectMany(candidate => candidate.HandlerAssemblies)
                    .Select(assembly => assembly.FullName));
            throw new InvalidOperationException(
                $"No inbox DbContext is registered for integration-event handler {handlerType.FullName}. " +
                $"Registered handler assemblies: {(string.IsNullOrWhiteSpace(available) ? "<none>" : available)}.");
        }

        return CreateStore(services, registration);
    }

    private static IInboxStore CreateStore(IServiceProvider services, Registration registration)
        => new EfInboxStore(
            (DbContext)services.GetRequiredService(registration.ContextType),
            services.GetService<Microsoft.Extensions.Logging.ILogger<EfInboxStore>>());

    private sealed record Registration(Type ContextType, Assembly[] HandlerAssemblies, Type[] HandlerTypes);
}

/// <summary>
/// Wraps a resolved handler in an inbox decorator using the DbContext owned by
/// the handler's module.
/// </summary>
internal sealed class InboxHandlerDecorator : IIntegrationEventHandlerDecorator
{
    public object Decorate(IServiceProvider services, Type eventType, object handler)
    {
        var decoratorType = typeof(IdempotentIntegrationEventHandler<>).MakeGenericType(eventType);
        var registry = services.GetService<InboxStoreRegistry>();
        var store = registry is null
            ? services.GetRequiredService<IInboxStore>()
            : registry.Resolve(services, handler.GetType());
        return ActivatorUtilities.CreateInstance(services, decoratorType, handler, store);
    }
}
