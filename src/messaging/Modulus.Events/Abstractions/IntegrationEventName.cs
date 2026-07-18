namespace Modulus.Events.Abstractions;

using System.Reflection;

/// <summary>
/// Pins a <b>stable, transport-level name</b> to an integration event, decoupling
/// the wire/persistence identity from the CLR type. This is the contract other
/// services and stored outbox rows depend on, so it must not change once shipped.
/// <code>
/// [IntegrationEventName("catalog.product-created.v1")]
/// public sealed record ProductCreated(Guid Id) : IntegrationEventBase("catalog.product-created.v1");
/// </code>
/// </summary>
/// <remarks>
/// Without this attribute the stable name falls back to the type's
/// <see cref="Type.FullName"/> (namespace-qualified, <b>assembly-independent</b>) —
/// safe against assembly version bumps but not against renames or namespace moves.
/// Apply the attribute (and version the name) as soon as an event crosses a
/// service boundary or is persisted, so a later refactor can't orphan in-flight
/// messages or unprocessed outbox rows.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Resolves the stable transport name of an integration event type — the single
/// source of truth for how events are keyed in the outbox, the registry, and on
/// the broker. Replaces the previous use of
/// <see cref="Type.AssemblyQualifiedName"/> (which broke on any assembly version
/// bump and was a <c>Type.GetType</c> deserialization surface).
/// </summary>
public static class IntegrationEventNaming
{
    /// <summary>
    /// The <see cref="IntegrationEventNameAttribute"/> value when present,
    /// otherwise the type's assembly-independent <see cref="Type.FullName"/>.
    /// </summary>
    public static string GetName(Type eventType)
        => eventType.GetCustomAttribute<IntegrationEventNameAttribute>(inherit: false)?.Name
           ?? eventType.FullName
           ?? eventType.Name;
}
