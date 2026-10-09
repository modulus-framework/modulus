using System.Collections.Concurrent;
using Modulus.Events.Abstractions;

namespace Modulus.Events;

public static class ModuleBusExtensions
{
    private static readonly ConcurrentDictionary<Type, Func<IModuleBus, IIntegrationEvent, CancellationToken, Task>> s_publishers = new();

    private static readonly System.Reflection.MethodInfo s_publishOf =
        typeof(ModuleBusExtensions).GetMethod(nameof(PublishTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    /// <summary>
    /// Publishes an event whose concrete type is only known at runtime (an outbox row, a broker message) through the
    /// generic <see cref="IModuleBus.PublishAsync{TEvent}"/>, with one cached delegate per event type instead of
    /// <c>dynamic</c> binding on every call.
    /// </summary>
    public static Task PublishBoxedAsync(this IModuleBus bus, IIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(@event);
        var publisher = s_publishers.GetOrAdd(@event.GetType(), static type =>
            (Func<IModuleBus, IIntegrationEvent, CancellationToken, Task>)s_publishOf.MakeGenericMethod(type)
                .CreateDelegate(typeof(Func<IModuleBus, IIntegrationEvent, CancellationToken, Task>)));
        return publisher(bus, @event, ct);
    }

    private static Task PublishTyped<TEvent>(IModuleBus bus, IIntegrationEvent @event, CancellationToken ct)
        where TEvent : IIntegrationEvent
        => bus.PublishAsync((TEvent)@event, ct);
}
