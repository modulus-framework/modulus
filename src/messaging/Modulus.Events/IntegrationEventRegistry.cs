namespace Modulus.Events;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Modulus.Events.Abstractions;

/// <summary>
/// Thread-safe registry that maps an integration event's <b>stable transport
/// name</b> (<see cref="IntegrationEventNaming.GetName"/> — an
/// <see cref="IntegrationEventNameAttribute"/> value, else the assembly-independent
/// <see cref="Type.FullName"/>) to its CLR type. This name is the routing key on
/// the broker and the identifier stored in outbox rows, so it stays valid across
/// assembly version bumps.
/// </summary>
public sealed class IntegrationEventRegistry : IIntegrationEventRegistry
{
    private readonly ConcurrentDictionary<string, Type> _byKey = new();

    public void Register(Type eventType)
    {
        if (!typeof(IIntegrationEvent).IsAssignableFrom(eventType))
            throw new InvalidOperationException(
                $"{eventType.FullName} does not implement {nameof(IIntegrationEvent)}.");

        _byKey[IntegrationEventNaming.GetName(eventType)] = eventType;
    }

    public bool TryGetType(string routingKey, [NotNullWhen(true)] out Type? eventType)
        => _byKey.TryGetValue(routingKey, out eventType);

    public IReadOnlyCollection<string> GetRoutingKeys() => _byKey.Keys.ToArray();
}
