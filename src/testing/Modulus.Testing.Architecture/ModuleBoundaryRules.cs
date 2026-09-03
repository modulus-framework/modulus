namespace Modulus.Testing.Architecture;

using System.Reflection;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;

/// <summary>
/// Architecture rules that enforce module boundaries in a Modulus modular monolith.
/// Use in xUnit tests to fail the build if boundaries are violated.
/// </summary>
/// <remarks>
/// Rules scan <b>all</b> non-dynamic assemblies loaded in the current app domain
/// (framework <c>Modulus.*</c> <i>and</i> app assemblies), so app-owned modules
/// and integration events are covered too.
/// </remarks>
public static class ModuleBoundaryRules
{
    /// <summary>
    /// Enforces that every concrete <see cref="IIntegrationEvent"/> implementation
    /// is decorated with <see cref="IntegrationEventNameAttribute"/>. Abstract base
    /// classes and interfaces are skipped.
    /// </summary>
    public static IReadOnlyList<Type> FindUnnamedIntegrationEvents()
    {
        var unnamed = new List<Type>();

        foreach (var type in GetScannableTypes())
        {
            if (typeof(IIntegrationEvent).IsAssignableFrom(type) &&
                !type.IsInterface &&
                !type.IsAbstract &&
                type.GetCustomAttribute<IntegrationEventNameAttribute>() is null)
            {
                unnamed.Add(type);
            }
        }

        return unnamed.AsReadOnly();
    }

    /// <summary>
    /// Enforces that all concrete <see cref="IModule"/> implementations can be
    /// discovered (and therefore instantiated by the host's explicit
    /// registration without instantiating them here).
    /// </summary>
    public static IReadOnlyList<Type> FindModuleTypes()
    {
        var moduleType = typeof(IModule);
        return GetScannableTypes()
            .Where(t => moduleType.IsAssignableFrom(t) &&
                       !t.IsInterface &&
                       !t.IsAbstract)
            .ToList()
            .AsReadOnly();
    }

    private static IEnumerable<Type> GetScannableTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(a =>
            {
                // ReflectionTypeLoadException-safe enumeration: a test assembly
                // graph may reference optional dependencies that are absent.
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.OfType<Type>();
                }
            });

}
