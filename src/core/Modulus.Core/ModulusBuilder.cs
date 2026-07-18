namespace Modulus.Core;

using System.Reflection;
using Modulus.Core.Abstractions;

/// <summary>
/// Fluent builder used inside <c>AddModulus(...)</c>.  Supports both explicit
/// module registration (<see cref="AddModule{TModule}"/>) and declarative
/// discovery via <see cref="DependsOnAttribute"/> (<see cref="AddModules{TStartupModule}"/>).
/// </summary>
/// <remarks>
/// Every module type is instantiated <b>exactly once</b>. The single instance is
/// used for dependency discovery (reading <see cref="IModule.DependsOn"/>), for
/// the three configuration phases, and is the same object registered in DI and
/// later initialized by <c>ModuleLoader</c>. Constructing a module twice (the
/// previous behaviour, which created a throwaway instance just to read its
/// dependencies) ran any constructor side effects twice.
/// <para>
/// Service configuration runs in three ordered phases inside <see cref="Complete"/>
/// — <see cref="IModule.PreConfigureServices"/> for every module, then
/// <see cref="IModule.ConfigureServices"/> for every module, then
/// <see cref="IModule.PostConfigureServices"/> for every module — each phase in
/// dependency order (dependencies first). Registration is therefore deferred until
/// <see cref="Complete"/> (always called by <c>AddModulus(...)</c>), which lets a
/// module in an earlier phase set up state that a later module refines.
/// </para>
/// </remarks>
public sealed class ModulusBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    // Single instance per module type. Shared by discovery and registration.
    private readonly Dictionary<Type, IModule> _instances = [];

    // Registered modules in registration order (dependencies first).
    internal List<IModule> Modules { get; } = [];

    public ModulusBuilder(
        IServiceCollection services,
        IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    /// <summary>
    /// Register a single module explicitly. The module's configuration phases run
    /// later, in <see cref="Complete"/>, batched with every other module.
    /// </summary>
    public ModulusBuilder AddModule<TModule>()
        where TModule : class, IModule, new()
        => AddModule(typeof(TModule));

    /// <summary>
    /// Register a single module by type.  Instantiates via parameterless
    /// constructor (once) and adds the instance to DI.  No-op if a module of the
    /// same type is already registered. The module's
    /// <see cref="IModule.PreConfigureServices"/>/<see cref="IModule.ConfigureServices"/>/
    /// <see cref="IModule.PostConfigureServices"/> phases do not run here — they run
    /// batched across all modules in <see cref="Complete"/>.
    /// </summary>
    public ModulusBuilder AddModule(Type moduleType)
    {
        if (!typeof(IModule).IsAssignableFrom(moduleType))
            throw new InvalidOperationException(
                $"{moduleType.FullName} does not implement {nameof(IModule)}.");

        // Skip if already registered (dedup during AddModules discovery)
        if (Modules.Any(m => m.GetType() == moduleType))
            return this;

        var module = GetOrCreate(moduleType);
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
    /// Runs the three service-configuration phases across every registered module
    /// (each in dependency order), then builds the module dependency graph and
    /// registers the fully-built <see cref="IModuleLoader"/> as a singleton
    /// instance. Called by <c>AddModulus(...)</c> after all modules are
    /// registered, so the graph is ready before the host starts — an app that
    /// forgets <c>UseModulus()</c> still initializes its modules.
    /// </summary>
    /// <remarks>
    /// Phase order is <see cref="IModule.PreConfigureServices"/> for all modules,
    /// then <see cref="IModule.ConfigureServices"/> for all modules, then
    /// <see cref="IModule.PostConfigureServices"/> for all modules.
    /// <see cref="Modules"/> is already in dependency order (topological for the
    /// discovery path, registration order for manual <see cref="AddModule(Type)"/>),
    /// so within each phase dependencies configure before dependents.
    /// </remarks>
    public IModuleLoader Complete()
    {
        foreach (var module in Modules)
            module.PreConfigureServices(_services, _configuration);

        foreach (var module in Modules)
            module.ConfigureServices(_services, _configuration);

        foreach (var module in Modules)
            module.PostConfigureServices(_services, _configuration);

        var loader = new ModuleLoader();
        loader.BuildGraph(Modules);
        _services.AddSingleton<IModuleLoader>(loader);
        return loader;
    }

    /// <summary>
    /// Returns (and caches) the single instance for <paramref name="moduleType"/>.
    /// </summary>
    private IModule GetOrCreate(Type moduleType)
    {
        if (_instances.TryGetValue(moduleType, out var existing))
            return existing;

        var module = (IModule)Activator.CreateInstance(moduleType)!;
        _instances[moduleType] = module;
        return module;
    }

    /// <summary>
    /// DFS that resolves [DependsOn] attributes recursively, producing a
    /// topologically-sorted list (dependencies first).
    /// </summary>
    private List<Type> DiscoverModuleTypes(Type root)
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
    /// implement IModule directly without inheriting ModulusModule. Reads the
    /// module's single cached instance — no throwaway construction.
    /// </summary>
    private IEnumerable<Type> GetDeclaredDependencies(Type moduleType)
    {
        // Read from [DependsOn] attributes
        var attrDeps = moduleType
            .GetCustomAttributes<DependsOnAttribute>(inherit: true)
            .SelectMany(a => a.Dependencies);

        // Also read from IModule.DependsOn property (for non-ModulusModule impls)
        if (typeof(IModule).IsAssignableFrom(moduleType) &&
            !moduleType.IsAbstract)
        {
            return attrDeps.Concat(GetOrCreate(moduleType).DependsOn).Distinct();
        }

        return attrDeps.Distinct();
    }
}
