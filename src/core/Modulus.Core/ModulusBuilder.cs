namespace Modulus.Core;

using System.Reflection;
using Modulus.Core.Abstractions;

/// <summary>
/// Fluent builder used inside <c>AddModulus(...)</c>.  Supports both explicit
/// module registration (<see cref="AddModule{TModule}"/>) and declarative
/// discovery via <see cref="DependsOnAttribute"/> (<see cref="AddModules{TStartupModule}"/>).
/// </summary>
public sealed class ModulusBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    internal List<IModule> Modules { get; } = [];

    public ModulusBuilder(
        IServiceCollection services,
        IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    /// <summary>
    /// Register a single module explicitly. Calls
    /// <see cref="IModule.ConfigureServices"/> immediately.
    /// </summary>
    public ModulusBuilder AddModule<TModule>()
        where TModule : class, IModule, new()
        => AddModule(typeof(TModule));

    /// <summary>
    /// Register a single module by type.  Instantiates via parameterless
    /// constructor, calls <see cref="IModule.ConfigureServices"/>, and adds
    /// the instance to DI.  No-op if a module of the same type is already
    /// registered.
    /// </summary>
    public ModulusBuilder AddModule(Type moduleType)
    {
        if (!typeof(IModule).IsAssignableFrom(moduleType))
            throw new InvalidOperationException(
                $"{moduleType.FullName} does not implement {nameof(IModule)}.");

        // Skip if already registered (dedup during AddModules discovery)
        if (Modules.Any(m => m.GetType() == moduleType))
            return this;

        var module = (IModule)Activator.CreateInstance(moduleType)!;
        module.ConfigureServices(_services, _configuration);
        Modules.Add(module);

        _services.AddSingleton(typeof(IModule), module);
        _services.AddSingleton(moduleType, module);
        return this;
    }

    /// <summary>
    /// Auto-discovers and registers the full module graph starting from
    /// <typeparamref name="TStartupModule"/>.  Walks
    /// <see cref="DependsOnAttribute"/>s recursively in topological order so
    /// that dependency modules register their services first.
    /// </summary>
    /// <typeparam name="TStartupModule">
    /// The root module — usually the application's startup module.
    /// </typeparam>
    public ModulusBuilder AddModules<TStartupModule>()
        where TStartupModule : class, IModule, new()
        => AddModules(typeof(TStartupModule));

    /// <summary>
    /// Auto-discovers and registers the full module graph starting from
    /// <paramref name="startupModuleType"/>.
    /// </summary>
    public ModulusBuilder AddModules(Type startupModuleType)
    {
        var ordered = DiscoverModuleTypes(startupModuleType);
        foreach (var type in ordered)
            AddModule(type);
        return this;
    }

    /// <summary>
    /// DFS that resolves [DependsOn] attributes recursively, producing a
    /// topologically-sorted list (dependencies first).
    /// </summary>
    private static List<Type> DiscoverModuleTypes(Type root)
    {
        var visited = new HashSet<Type>();
        var ordered = new List<Type>();
        var inStack = new HashSet<Type>();

        void Visit(Type type)
        {
            if (visited.Contains(type)) return;
            if (inStack.Contains(type))
                throw new InvalidOperationException(
                    $"Circular module dependency detected at {type.FullName}.");

            inStack.Add(type);

            foreach (var dep in GetDeclaredDependencies(type))
                Visit(dep);

            inStack.Remove(type);
            visited.Add(type);
            ordered.Add(type);
        }

        Visit(root);
        return ordered;
    }

    /// <summary>
    /// Gets dependency module types declared via [DependsOn] attributes,
    /// falling back to the IModule.DependsOn property for modules that
    /// implement IModule directly without inheriting ModulusModule.
    /// </summary>
    private static IEnumerable<Type> GetDeclaredDependencies(Type moduleType)
    {
        // Read from [DependsOn] attributes
        var attrDeps = moduleType
            .GetCustomAttributes<DependsOnAttribute>(inherit: true)
            .SelectMany(a => a.Dependencies);

        // Also read from IModule.DependsOn property (for non-ModulusModule impls)
        if (typeof(IModule).IsAssignableFrom(moduleType) &&
            !moduleType.IsAbstract)
        {
            var instance = (IModule)Activator.CreateInstance(moduleType)!;
            return attrDeps.Concat(instance.DependsOn).Distinct();
        }

        return attrDeps.Distinct();
    }
}