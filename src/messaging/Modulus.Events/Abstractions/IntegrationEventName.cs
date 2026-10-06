namespace Modulus.Events.Abstractions;

using System.Reflection;
using System.Text;

/// <summary>
/// Pins a <b>stable, transport-level name</b> to an integration event, decoupling the
/// wire/persistence identity from the CLR type. This is the contract other services and
/// stored outbox rows depend on, so it must not change once shipped.
/// </summary>
/// <remarks>
/// Prefer <see cref="IntegrationEventAttribute{TModule}"/>, which derives the name from
/// types (no string literal). This legacy single-string form exists for names that are
/// already persisted or consumed elsewhere and must be kept verbatim; it should read
/// <c>module.event.vN</c>. Without any attribute the name falls back to the type's
/// <see cref="Type.FullName"/>, which a rename or namespace move would break.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventNameAttribute : Attribute
{
    /// <summary>Uses <paramref name="name"/> verbatim.</summary>
    public IntegrationEventNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The stable transport name.</summary>
    public string Name { get; }
}

/// <summary>Non-generic view of <see cref="IntegrationEventAttribute{TModule}"/> so the framework can read it.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class IntegrationEventAttributeBase : Attribute
{
    private protected IntegrationEventAttributeBase()
    {
    }

    /// <summary>The module marker type the event belongs to.</summary>
    public abstract Type ModuleType { get; }

    /// <summary>The contract version (<c>v1</c> by default). Bump it for a breaking payload change.</summary>
    public int Version
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1;
}

/// <summary>
/// Declares an integration event's stable name <b>from types, with no string literal</b>:
/// <code>
/// public sealed class PaymentsArea;                                   // module marker, once per module
///
/// [IntegrationEvent&lt;PaymentsArea&gt;]                                   // payments.subscription-purchased.v1
/// public sealed record SubscriptionPurchased(Guid Id) : IntegrationEventBase;
///
/// [IntegrationEvent&lt;PaymentsArea&gt;(Version = 2)]                       // payments.subscription-purchased.v2
/// public sealed record SubscriptionPurchasedV2(Guid Id) : IntegrationEventBase;
/// </code>
/// The module part comes from <typeparamref name="TModule"/>'s name (a trailing <c>Module</c>, <c>Area</c> or
/// <c>Marker</c> is dropped) and the event part from the event type's name (a trailing <c>IntegrationEvent</c> or
/// <c>Event</c> is dropped), both converted to kebab-case. Because renaming the event type would change the wire name,
/// the contract snapshot rule (<c>ModuleBoundaryRules.FindIntegrationEventContractChanges</c>) fails the build until the
/// change is acknowledged; the analyzer MOD0003 covers the rest.
/// </summary>
/// <typeparam name="TModule">A marker type naming the owning module (any type in a layer the event can see).</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute<TModule> : IntegrationEventAttributeBase
{
    /// <inheritdoc />
    public override Type ModuleType => typeof(TModule);
}

/// <summary>
/// Resolves the stable transport name of an integration event type — the single source of truth for how events are
/// keyed in the outbox, the registry, and on the broker.
/// </summary>
public static class IntegrationEventNaming
{
    private static readonly string[] s_moduleSuffixes = ["Module", "Area", "Marker"];
    private static readonly string[] s_eventSuffixes = ["IntegrationEvent", "Event"];

    /// <summary>
    /// The declared name: an <see cref="IntegrationEventAttribute{TModule}"/> derivation, else an
    /// <see cref="IntegrationEventNameAttribute"/> value, else the type's assembly-independent <see cref="Type.FullName"/>.
    /// </summary>
    public static string GetName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (eventType.GetCustomAttributes(typeof(IntegrationEventAttributeBase), inherit: false)
                .OfType<IntegrationEventAttributeBase>().FirstOrDefault() is { } derived)
            return Derive(derived.ModuleType.Name, eventType.Name, derived.Version);

        return eventType.GetCustomAttribute<IntegrationEventNameAttribute>(inherit: false)?.Name
               ?? eventType.FullName
               ?? eventType.Name;
    }

    /// <summary>True when the type declares its name with either attribute (so it does not rely on the CLR full name).</summary>
    public static bool HasDeclaredName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return eventType.IsDefined(typeof(IntegrationEventAttributeBase), inherit: false)
               || eventType.IsDefined(typeof(IntegrationEventNameAttribute), inherit: false);
    }

    /// <summary>Builds <c>{module}.{event}.v{version}</c> from a module marker's and an event's type names.</summary>
    public static string Derive(string moduleTypeName, string eventTypeName, int version = 1)
        => $"{Kebab(Strip(moduleTypeName, s_moduleSuffixes))}.{Kebab(Strip(eventTypeName, s_eventSuffixes))}.v{version}";

    /// <summary><c>SubscriptionPurchased</c> → <c>subscription-purchased</c>; <c>HTTPServerError</c> → <c>http-server-error</c>.</summary>
    public static string Kebab(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (!char.IsLetterOrDigit(c))
                continue;

            if (char.IsUpper(c) && builder.Length > 0)
            {
                var previousLower = char.IsLower(pascal[i - 1]) || char.IsDigit(pascal[i - 1]);
                var acronymEnds = char.IsUpper(pascal[i - 1]) && i + 1 < pascal.Length && char.IsLower(pascal[i + 1]);
                if (previousLower || acronymEnds)
                    builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string Strip(string name, string[] suffixes)
    {
        // A generic arity suffix (`1) is never part of the name.
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            name = name[..tick];

        foreach (var suffix in suffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        }

        return name;
    }
}
