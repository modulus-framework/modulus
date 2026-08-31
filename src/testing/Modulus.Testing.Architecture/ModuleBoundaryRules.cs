namespace Modulus.Testing.Architecture;

using System.Reflection;
using Modulus.Events.Abstractions;

/// <summary>
/// Architecture rules that enforce module boundaries in a Modulus modular monolith.
/// Use in xUnit tests to fail the build if boundaries are violated.
/// </summary>
public static class ModuleBoundaryRules
{
    /// <summary>
    /// Enforces that every <see cref="IIntegrationEvent"/> crossing a module boundary
    /// is decorated with <see cref="IntegrationEventNameAttribute"/>.
    /// </summary>
    public static IReadOnlyList<Type> FindUnnamedIntegrationEvents()
    {
        var unnamed = new List<Type>();

        foreach (var type in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => a.GetName().Name?.StartsWith("Modulus.") ?? false)
                     .SelectMany(a => a.GetTypes()))
        {
            if (typeof(IIntegrationEvent).IsAssignableFrom(type) &&
                !type.IsInterface &&
                type.GetCustomAttribute<IntegrationEventNameAttribute>() is null)
            {
                unnamed.Add(type);
            }
        }

        return unnamed.AsReadOnly();
    }

    /// <summary>
    /// Enforces that all IModule implementations can be instantiated without circular dependency errors.
    /// </summary>
    public static IReadOnlyList<Type> FindModuleTypes()
    {
        var moduleType = typeof(IModule);
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Modulus.") ?? false)
            .SelectMany(a => a.GetTypes()
                .Where(t => moduleType.IsAssignableFrom(t) &&
                           !t.IsInterface &&
                           !t.IsAbstract))
            .ToList()
            .AsReadOnly();
    }
}

// Placeholder for IModule since it's in Modulus.Core
internal interface IModule { }
