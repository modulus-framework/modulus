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

    /// <summary>
    /// Detects domain types (in .Domain.* namespaces) that are referenced by
    /// types in other modules. Domain types should only be used within their
    /// module for command/query handling, not leaked as return types or parameters.
    /// </summary>
    /// <returns>
    /// List of violations: (violating type, domain type it references).
    /// </returns>
    public static IReadOnlyList<(Type ViolatingType, Type DomainType)>
        FindCrossModuleDomainTypeUsage()
    {
        var violations = new List<(Type, Type)>();
        var allTypes = GetScannableTypes().ToList();
        var domainTypes = allTypes.Where(t => IsInDomainLayer(t)).ToList();

        foreach (var type in allTypes)
        {
            // Skip types without a module (not part of Modulus or app structure).
            if (ExtractModuleName(type) is null)
                continue;

            // Scan for references to domain types outside this type's module.
            var usedTypes = GetReferencedTypes(type);
            foreach (var usedType in usedTypes)
            {
                if (domainTypes.Contains(usedType) &&
                    ExtractModuleName(type) != ExtractModuleName(usedType))
                {
                    violations.Add((type, usedType));
                }
            }
        }

        return violations.AsReadOnly();
    }

    /// <summary>
    /// Detects DbContext types that are referenced by types in other modules.
    /// DbContext is an implementation detail; modules should communicate through
    /// the application/domain layer (queries, commands, integration events).
    /// </summary>
    /// <returns>
    /// List of violations: (violating type, DbContext it references).
    /// </returns>
    public static IReadOnlyList<(Type ViolatingType, Type DbContextType)>
        FindCrossModuleDbContextUsage()
    {
        var violations = new List<(Type, Type)>();
        var allTypes = GetScannableTypes().ToList();

        // Load DbContext type dynamically; if not available, skip the check.
        var dbContextType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException) { return []; }
            })
            .FirstOrDefault(t => t.FullName == "Microsoft.EntityFrameworkCore.DbContext");

        if (dbContextType is null)
            return violations.AsReadOnly();

        foreach (var type in allTypes)
        {
            if (ExtractModuleName(type) is null)
                continue;

            var usedTypes = GetReferencedTypes(type);
            foreach (var usedType in usedTypes)
            {
                if (dbContextType.IsAssignableFrom(usedType) &&
                    ExtractModuleName(type) != ExtractModuleName(usedType))
                {
                    violations.Add((type, usedType));
                }
            }
        }

        return violations.AsReadOnly();
    }

    // Extract module name from namespace: TradeFlow.Modules.Budgeting.* → "Budgeting"
    private static string? ExtractModuleName(Type type)
    {
        var ns = type.Namespace ?? "";
        if (!ns.Contains(".Modules."))
            return null;

        var parts = ns.Split('.');
        var modulesIdx = Array.IndexOf(parts, "Modules");
        if (modulesIdx >= 0 && modulesIdx + 1 < parts.Length)
            return parts[modulesIdx + 1];

        return null;
    }

    // Check if type is in a .Domain.* namespace
    private static bool IsInDomainLayer(Type type) =>
        (type.Namespace ?? "").Contains(".Domain.");

    // Collect all types referenced in method signatures, properties, fields
    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        var referenced = new HashSet<Type>();

        // Method return types and parameters
        foreach (var method in type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            referenced.Add(method.ReturnType);
            foreach (var param in method.GetParameters())
                referenced.Add(param.ParameterType);
        }

        // Property types
        foreach (var prop in type.GetProperties(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            referenced.Add(prop.PropertyType);
        }

        // Field types
        foreach (var field in type.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            referenced.Add(field.FieldType);
        }

        // Unwrap generics and arrays
        return referenced
            .Where(t => t != null && t != typeof(void))
            .Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)
            .Select(t => t.IsArray ? t.GetElementType()! : t)
            .Where(t => t != typeof(object) && t != typeof(string) && !t.IsValueType)
            .Distinct();
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
