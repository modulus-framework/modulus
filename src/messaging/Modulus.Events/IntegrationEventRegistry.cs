namespace Modulus.Events;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Modulus.Events.Abstractions;

/// <summary>
/// Thread-safe registry that maps broker routing keys to integration-event CLR types.
/// The routing key is the event type's <see cref="Type.FullName"/> — stable across
/// publisher and consumer as long as both share the same namespace conventions.
/// </summary>
public sealed class IntegrationEventRegistry : IIntegrationEventRegistry
{
    private readonly ConcurrentDictionary<string, Type> _byKey = new();

    public void Register(Type eventType)
    {
        if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
            throw new InvalidOperationException(
                $"{eventType.FullName} does not implement {nameof(IIntegrationEvent)}.");

        _byKey[eventType.FullName!] = eventType;
    }

    public bool TryGetType(string routingKey, [NotNullWhen(true)] out Type? eventType)
        => _byKey.TryGetValue(routingKey, out eventType);

    public IReadOnlyCollection<string> GetRoutingKeys() => _byKey.Keys.ToArray();
}
