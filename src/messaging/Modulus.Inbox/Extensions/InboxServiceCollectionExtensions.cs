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
    /// Wraps all <c>IIntegrationEventHandler{T}</c> dispatch with the
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
    /// Registers the inbox's dispatch-time handler decorator (idempotent —
    /// safe to call any number of times, e.g. once per module's
    /// <c>AddInbox</c>/<c>AddMongoInbox</c> call). Called by
    /// <c>AddInbox</c> and <c>AddMongoInbox</c> after registering the
    /// appropriate <see cref="IInboxStore"/> implementation.
    /// </summary>
    /// <remarks>
    /// Despite the name (kept for source compatibility), this no longer
    /// mutates any <c>IIntegrationEventHandler{T}</c> service descriptors.
    /// That approach depended on every handler already being registered by
    /// the moment this ran — which depends on where <c>AddModulusEvents(...)</c>
    /// happens to sit relative to <c>AddModulus(...)</c>/<c>AddInbox(...)</c>
    /// in <c>Program.cs</c>, and broke outright when <c>AddInbox</c> is called
    /// more than once (each call re-wrapped the already-wrapped descriptors
    /// from the previous call, nesting decorators that raced each other's
    /// claims). It now just registers <see cref="InboxHandlerDecorator"/>
    /// (<c>TryAddSingleton</c>, so repeat calls are inert); the dispatchers
    /// (<see cref="Modulus.Events.IntegrationEventDispatcher"/>,
    /// <see cref="Modulus.Events.InProcessModuleBus"/>) wrap each handler they
    /// resolve at the moment they dispatch, which is always after every
    /// handler AND every inbox registration has run, regardless of order.
    /// </remarks>
    public static IServiceCollection DecorateIntegrationEventHandlers(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IIntegrationEventHandlerDecorator, InboxHandlerDecorator>();
        return services;
    }
}

/// <summary>
/// Wraps a resolved <c>IIntegrationEventHandler&lt;TEvent&gt;</c> in
/// <see cref="IdempotentIntegrationEventHandler{TEvent}"/>. Stateless
/// (registered as a singleton); the scoped services the decorator itself
/// needs (<see cref="IInboxStore"/>, options, logger) are resolved from the
/// <see cref="IServiceProvider"/> passed in at dispatch time, not captured
/// here.
/// </summary>
internal sealed class InboxHandlerDecorator : IIntegrationEventHandlerDecorator
{
    public object Decorate(IServiceProvider services, Type eventType, object handler)
    {
        var decoratorType = typeof(IdempotentIntegrationEventHandler<>).MakeGenericType(eventType);
        return ActivatorUtilities.CreateInstance(services, decoratorType, handler);
    }
}
