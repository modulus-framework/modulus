namespace Modulus.Core;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

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

    // Registered modules in registration order (dependencies first after
    // Complete() sorts them; registration order before that).
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
    /// Before the phases run, <see cref="Modules"/> is topologically sorted via
    /// the shared <see cref="ModuleGraph"/> engine — this also covers manual
    /// <see cref="AddModule(Type)"/> calls made in dependency-before-dependent
    /// order, which previously ran unsorted. Every declared dependency must be
    /// registered by this point; a missing one throws
    /// <see cref="ModuleNotFoundException"/>.
    /// </remarks>
    public IModuleLoader Complete()
    {
        SortModulesTopologically();

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
    /// Reorders <see cref="Modules"/> into topological order (dependencies
    /// first) using the same engine as discovery and lifecycle ordering, so all
    /// three orders always agree — including manually-registered modules that
    /// were added dependent-first.
    /// </summary>
    private void SortModulesTopologically()
    {
        var sortedTypes = ModuleGraph.Sort(
            Modules.Select(m => m.GetType()),
            type => RegisteredRequiredDeps(type));

        var sortedModules = new List<IModule>(sortedTypes.Count);
        foreach (var type in sortedTypes)
            sortedModules.Add(GetOrCreate(type));

        Modules.Clear();
        Modules.AddRange(sortedModules);
    }

    /// <summary>
    /// Required dependencies of a registered module; every dependency must be
    /// registered by <see cref="Complete"/> time, otherwise startup fails with
    /// a descriptive error naming the declaring module.
    /// </summary>
    private IEnumerable<Type> RegisteredRequiredDeps(Type moduleType)
    {
        foreach (var dep in DeclaredDeps(moduleType))
        {
            if (!Modules.Any(m => m.GetType() == dep))
                throw new ModuleNotFoundException(dep, moduleType);

            yield return dep;
        }
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
    /// Topologically-sorts the module graph reachable from <paramref name="root"/>
    /// via required dependencies (optional dependencies are not discovered),
    /// delegating to the shared <see cref="ModuleGraph"/> engine.
    /// </summary>
    private List<Type> DiscoverModuleTypes(Type root)
        => [.. ModuleGraph.Sort([root], DeclaredDeps)];

    /// <summary>
    /// Required dependencies declared via non-optional [DependsOn] attributes
    /// plus the <see cref="IModule.DependsOn"/> property, read through the
    /// cached <see cref="ModuleGraph"/> resolver. Optional attribute
    /// dependencies are excluded — they never pull unregistered modules into
    /// the graph during discovery.
    /// </summary>
    private IEnumerable<Type> DeclaredDeps(Type moduleType)
    {
        foreach (var dep in ModuleGraph.GetRequiredAttributeDeps(moduleType))
            yield return dep;

        // Also read from IModule.DependsOn property (for non-ModulusModule impls)
        if (typeof(IModule).IsAssignableFrom(moduleType) &&
            !moduleType.IsAbstract)
        {
            foreach (var dep in GetOrCreate(moduleType).DependsOn)
                yield return dep;
        }
    }
}
