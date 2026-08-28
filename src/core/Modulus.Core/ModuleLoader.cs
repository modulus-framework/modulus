namespace Modulus.Core;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

public sealed class ModuleLoader : IModuleLoader
{
    private IReadOnlyList<ModuleDescriptor> _sorted = [];
    private IReadOnlyList<IModule> _modules = [];
    private Dictionary<Type, IModule> _modulesByType = [];

    // ── BuildGraph ────────────────────────────────────────────────
    /// <summary>
    /// Topologically sorts the given modules (dependencies first) using the
    /// shared <see cref="ModuleGraph"/> engine — the same engine used for
    /// registration discovery and configuration-phase ordering, so all orders
    /// always agree. Optional <see cref="DependsOnAttribute"/> dependencies
    /// order a registered module before its dependent but never fail startup
    /// when absent.
    /// </summary>
    /// <exception cref="CircularDependencyException">The graph contains a cycle.</exception>
    /// <exception cref="ModuleNotFoundException">
    /// A module declares a required dependency that is not in <paramref name="modules"/>.
    /// </exception>
    public IReadOnlyList<ModuleDescriptor> BuildGraph(
        IEnumerable<IModule> modules)
    {
        _modules = modules.ToList();
        _modulesByType = BuildModuleMap(_modules);
        var registeredTypes = _modulesByType.Keys.ToHashSet();

        var sortedTypes = ModuleGraph.Sort(
            _modulesByType.Keys,
            type => RequiredAndOptionalPresent(map: _modulesByType, type));

        var sorted = new List<ModuleDescriptor>(sortedTypes.Count);
        int order = 0;
        foreach (var type in sortedTypes)
        {
            var module = _modulesByType[type];
            sorted.Add(new ModuleDescriptor
            {
                Name = type.Name,
                ModuleType = type,
                Dependencies = DeclaredDependencies(module),
                InitOrder = order++,
            });
        }

        _sorted = sorted.AsReadOnly();
        return _sorted;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModuleDescriptor> GetDescriptors() => _sorted;

    // ── InitializeAllAsync ────────────────────────────────────────
    public async Task InitializeAllAsync(
        IServiceProvider sp,
        CancellationToken ct = default)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ModuleLoader>();

        logger.LogInformation("[Modulus] Initializing {Count} modules...",
            _sorted.Count);

        foreach (var descriptor in _sorted)
        {
            var module = (IModule)sp.GetRequiredService(descriptor.ModuleType);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var ctx = new ModuleContext
            {
                ServiceProvider = sp,
                Configuration = sp.GetRequiredService<IConfiguration>(),
                Logger = sp.GetRequiredService<ILoggerFactory>()
                           .CreateLogger(descriptor.ModuleType),
                Descriptor = descriptor,
            };

            await module.InitializeAsync(ctx, ct);
            sw.Stop();

            logger.LogInformation(
                "[Modulus] {Module} initialized ({Ms}ms)",
                descriptor.Name, sw.ElapsedMilliseconds);
        }

        logger.LogInformation("[Modulus] All {Count} modules ready.",
            _sorted.Count);
    }

    // ── ShutdownAllAsync ──────────────────────────────────────────
    public async Task ShutdownAllAsync(CancellationToken ct = default)
    {
        foreach (var descriptor in _sorted.Reverse())
        {
            var module = _modulesByType[descriptor.ModuleType];
            await module.ShutdownAsync(ct);
        }
    }

    /// <summary>
    /// Required dependencies (attributes ∪ property) followed by any optional
    /// attribute dependencies that are present in <paramref name="map"/> — an
    /// unregistered optional dependency is silently skipped instead of failing
    /// startup.
    /// </summary>
    private static IEnumerable<Type> RequiredAndOptionalPresent(
        IReadOnlyDictionary<Type, IModule> map,
        Type type)
    {
        var module = map[type];

        foreach (var dep in ModuleGraph.RequiredDeps(module))
        {
            if (!map.ContainsKey(dep))
                throw new ModuleNotFoundException(dep, type);

            yield return dep;
        }

        foreach (var dep in ModuleGraph.GetOptionalAttributeDeps(type))
        {
            if (map.ContainsKey(dep))
                yield return dep;
        }
    }

    /// <summary>All declared dependencies (required + optional present), for reporting.</summary>
    private static Type[] DeclaredDependencies(IModule module)
        => ModuleGraph.RequiredDeps(module)
            .Concat(ModuleGraph.GetOptionalAttributeDeps(module.GetType()))
            .Distinct()
            .ToArray();

    /// <summary>
    /// Indexes modules by concrete type; duplicate registrations fail with a
    /// descriptive exception instead of a bare <see cref="ArgumentException"/>.
    /// </summary>
    private static Dictionary<Type, IModule> BuildModuleMap(IReadOnlyList<IModule> modules)
    {
        var map = new Dictionary<Type, IModule>(modules.Count);
        foreach (var module in modules)
        {
            var type = module.GetType();
            if (!map.TryAdd(type, module))
                throw new InvalidOperationException(
                    $"Module type {type.FullName} was registered more than once. " +
                    "Each module type may only appear once in the graph.");
        }

        return map;
    }
}
