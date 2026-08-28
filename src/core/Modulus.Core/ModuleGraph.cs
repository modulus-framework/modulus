namespace Modulus.Core;

using System.Collections.Concurrent;
using System.Reflection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Single source of truth for the module dependency graph: reads
/// <see cref="DependsOnAttribute"/>s (cached per type), validates dependency
/// declarations, and performs the topological sort used by both module
/// discovery (<see cref="ModulusBuilder.AddModules(Type)"/>) and lifecycle
/// ordering (<see cref="ModuleLoader.BuildGraph"/>).
/// </summary>
/// <remarks>
/// Previously the DFS/cycle detection and attribute reflection existed twice —
/// once in <see cref="ModulusBuilder"/> and again in <see cref="ModuleLoader"/>
/// — with divergent exception types. Both paths now delegate here so
/// registration order, configuration-phase order, and initialisation order can
/// never disagree.
/// </remarks>
internal static class ModuleGraph
{
    // Attribute reflection is cached for the process lifetime — module types
    // are static and their [DependsOn] declarations cannot change.
    private static readonly ConcurrentDictionary<Type, DependsOnAttribute[]> AttributeCache = new();

    /// <summary>Returns the cached <see cref="DependsOnAttribute"/>s on a module type.</summary>
    public static DependsOnAttribute[] GetAttributes(Type moduleType)
        => AttributeCache.GetOrAdd(
            moduleType,
            static t => t.GetCustomAttributes<DependsOnAttribute>(inherit: true).ToArray());

    /// <summary>Required dependencies declared via non-optional <see cref="DependsOnAttribute"/>s.</summary>
    public static IEnumerable<Type> GetRequiredAttributeDeps(Type moduleType)
        => GetAttributes(moduleType)
            .Where(a => !a.Optional)
            .SelectMany(a => a.Dependencies)
            .Distinct();

    /// <summary>Optional dependencies declared via <see cref="DependsOnAttribute"/>s with <c>Optional = true</c>.</summary>
    public static IEnumerable<Type> GetOptionalAttributeDeps(Type moduleType)
        => GetAttributes(moduleType)
            .Where(a => a.Optional)
            .SelectMany(a => a.Dependencies)
            .Distinct();

    /// <summary>
    /// Combined required dependencies for a live module instance: non-optional
    /// <see cref="DependsOnAttribute"/>s ∪ the <see cref="IModule.DependsOn"/>
    /// property. The union preserves the existing semantics — an override of
    /// <see cref="IModule.DependsOn"/> adds programmatic dependencies on top of
    /// attribute-declared ones rather than replacing them.
    /// </summary>
    public static IEnumerable<Type> RequiredDeps(IModule module)
        => GetRequiredAttributeDeps(module.GetType()).Concat(module.DependsOn).Distinct();

    /// <summary>
    /// Validates that a declared dependency can actually serve as a module:
    /// it must be a concrete <see cref="IModule"/> implementation.
    /// </summary>
    public static void ValidateDependency(Type declaringModule, Type dependency)
    {
        if (!typeof(IModule).IsAssignableFrom(dependency))
        {
            throw new InvalidModuleDependencyException(
                declaringModule,
                dependency,
                $"it does not implement {nameof(IModule)}");
        }

        if (dependency.IsAbstract)
        {
            throw new InvalidModuleDependencyException(
                declaringModule,
                dependency,
                "it is abstract and cannot be instantiated");
        }
    }

    /// <summary>
    /// Topologically sorts <paramref name="roots"/> and every transitively
    /// required module (dependencies first). Throws
    /// <see cref="CircularDependencyException"/> (with the full cycle path) or
    /// <see cref="InvalidModuleDependencyException"/>. Optional dependencies are
    /// NOT traversed — callers decide whether to include them.
    /// </summary>
    /// <param name="roots">Entry-point module types.</param>
    /// <param name="requiredDeps">
    /// Returns the required dependencies of a type. Callers that require every
    /// dependency to be pre-registered throw <see cref="ModuleNotFoundException"/>
    /// from this callback; discovery returns unregistered types so they get visited.
    /// </param>
    public static IReadOnlyList<Type> Sort(
        IEnumerable<Type> roots,
        Func<Type, IEnumerable<Type>> requiredDeps)
    {
        var visited = new HashSet<Type>();
        var stack = new List<Type>();
        var ordered = new List<Type>();

        void Visit(Type type)
        {
            if (visited.Contains(type))
            {
                return;
            }

            var cycleStart = stack.IndexOf(type);
            if (cycleStart >= 0)
            {
                var cycle = stack.Skip(cycleStart).ToList();
                cycle.Add(type);
                throw new CircularDependencyException(cycle);
            }

            stack.Add(type);

            foreach (var dep in requiredDeps(type))
            {
                ValidateDependency(declaringModule: type, dependency: dep);
                Visit(dep);
            }

            stack.RemoveAt(stack.Count - 1);
            visited.Add(type);
            ordered.Add(type);
        }

        foreach (var root in roots)
        {
            Visit(root);
        }

        return ordered;
    }
}
