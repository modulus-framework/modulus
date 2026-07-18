using System.Diagnostics.CodeAnalysis;

namespace Modulus.Events.Abstractions;

/// <summary>
/// Maps integration-event CLR types to their broker routing keys and vice-versa.
/// Populated at startup by scanning assemblies for <see cref="IIntegrationEvent"/>
/// implementations that have at least one registered handler.
/// </summary>
public interface IIntegrationEventRegistry
{
    /// <summary>Register an event type and derive its routing key.</summary>
    void Register(Type eventType);

    /// <summary>Resolve the CLR type from a routing key (topic / routing-key).</summary>
    bool TryGetType(string routingKey, [NotNullWhen(true)] out Type? eventType);

    /// <summary>All routing keys this application is interested in consuming.</summary>
    IReadOnlyCollection<string> GetRoutingKeys();
}
