using System.Reflection;

namespace Modulus.Messaging;

/// <summary>
/// The message types this process will deserialize (NFR-10). A type name that arrives on the wire is resolved only
/// when it names a registered message type, or a concrete type assignable to one (a publish of a derived type reaches
/// a consumer of its base type or interface). Any other name is refused before a byte of the body is read, so a
/// message can never make the bus instantiate a type the application did not register.
/// </summary>
/// <remarks>
/// The set is built on first use from the registered message types, by scanning the loaded assemblies. It is rebuilt
/// for assemblies that load later, so a derived type from a late-loaded assembly is found; a type whose assembly is
/// never loaded in this process cannot be resolved, which is the intended refusal.
/// </remarks>
internal sealed class MessageTypeAllowList
{
    private readonly Func<IEnumerable<Type>> _roots;
    private readonly object _gate = new();
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly HashSet<Assembly> _scanned = [];
    private readonly HashSet<string> _ambiguous = new(StringComparer.Ordinal);
    private IReadOnlyList<Type> _rootTypes = [];
    private bool _initialised;

    public MessageTypeAllowList(Func<IEnumerable<Type>> registeredMessageTypes)
    {
        _roots = registeredMessageTypes ?? throw new ArgumentNullException(nameof(registeredMessageTypes));
    }

    /// <summary>
    /// Resolves a type name received from the wire (NFR-10). A bus-hosted transport always passes its allow-list. A
    /// transport used on its own has no registered types and falls back to the framework's name resolution.
    /// </summary>
    internal static Type? ResolveWireType(string? typeName, MessageTypeAllowList? allowList)
    {
        if (typeName is null)
        {
            return null;
        }

        if (allowList is not null)
        {
            return allowList.Resolve(typeName);
        }

        // Standalone: a URN has no registry to consult, so find the loaded type that carries it. A .NET name resolves as before.
        return typeName.StartsWith(MessageUrn.UrnPrefix, StringComparison.Ordinal) ? FindByUrn(typeName) : Type.GetType(typeName);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type> StandaloneUrns = new(StringComparer.Ordinal);

    private static Type? FindByUrn(string urn)
    {
        if (StandaloneUrns.TryGetValue(urn, out var cached))
        {
            return cached;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            foreach (var type in TypesOf(assembly))
            {
                if (type.IsClass && MessageUrn.Of(type) == urn)
                {
                    StandaloneUrns[urn] = type;
                    return type;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the registered type with this assembly-qualified name, or null when it is not allowed.</summary>
    public Type? Resolve(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        lock (_gate)
        {
            if (_byName.TryGetValue(typeName, out var known))
            {
                return known;
            }

            // A name that is not known may belong to an assembly loaded since the last scan: scan the new ones once.
            Scan();
            return _byName.GetValueOrDefault(typeName);
        }
    }

    private void Scan()
    {
        if (!_initialised)
        {
            _rootTypes = _roots().Distinct().ToArray();
            _initialised = true;
            foreach (var root in _rootTypes)
            {
                AddIfAllowed(root);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || !_scanned.Add(assembly))
            {
                continue;
            }

            foreach (var type in TypesOf(assembly))
            {
                if (type.IsClass && !type.IsAbstract && _rootTypes.Any(root => root.IsAssignableFrom(type)))
                {
                    AddIfAllowed(type);
                }
            }
        }
    }

    /// <summary>
    /// Keys the type by its URN (RULE-10). The assembly-qualified name is accepted too, so a message published by an
    /// older build during a rolling deploy still decodes. A URN claimed by two types is refused, not guessed.
    /// </summary>
    private void AddIfAllowed(Type type)
    {
        var urn = MessageUrn.Of(type);
        if (_byName.TryGetValue(urn, out var owner))
        {
            if (owner != type)
            {
                _ambiguous.Add(urn);
                _byName.Remove(urn);
            }
        }
        else if (!_ambiguous.Contains(urn))
        {
            _byName[urn] = type;
        }

        if (type.AssemblyQualifiedName is { } name)
        {
            _byName.TryAdd(name, type);
        }
    }

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }
}
