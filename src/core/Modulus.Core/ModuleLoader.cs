namespace Modulus.Core;

using System.Reflection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

public sealed class ModuleLoader : IModuleLoader
{
    private IReadOnlyList<ModuleDescriptor> _sorted = [];
    private IReadOnlyList<IModule> _modules = [];

    // ── BuildGraph ────────────────────────────────────────────────
    public IReadOnlyList<ModuleDescriptor> BuildGraph(
        IEnumerable<IModule> modules)
    {
        _modules = modules.ToList();
        var map = _modules.ToDictionary(m => m.GetType());
        var visited = new HashSet<Type>();
        var inStack = new HashSet<Type>();
        var sorted = new List<ModuleDescriptor>();
        int order = 0;

        void Visit(IModule module)
        {
            var t = module.GetType();
            if (inStack.Contains(t))
                throw new CircularDependencyException(t);
            if (visited.Contains(t)) return;

            inStack.Add(t);

            // Read dependencies from BOTH [DependsOn] attributes and the
            // IModule.DependsOn property — same source as ModulusBuilder, so
            // registration and initialization ordering always agree.
            foreach (var dep in GetCombinedDependencies(module, map))
            {
                if (!map.TryGetValue(dep, out var depModule))
                    throw new ModuleNotFoundException(dep);
                Visit(depModule);
            }

            inStack.Remove(t);
            visited.Add(t);
            sorted.Add(new ModuleDescriptor
            {
                Name = t.Name,
                ModuleType = t,
                Dependencies = GetCombinedDependencies(module, map).ToArray(),
                InitOrder = order++,
            });
        }

        foreach (var m in _modules) Visit(m);
        _sorted = sorted.AsReadOnly();
        return _sorted;
    }

    /// <summary>
    /// Returns the union of [DependsOn] attribute dependencies and
    /// <see cref="IModule.DependsOn"/> property dependencies.
    /// This mirrors <see cref="ModulusBuilder.GetDeclaredDependencies"/>
    /// so that the builder (registration) and the loader (init ordering)
    /// always resolve the same dependency graph.
    /// </summary>
    private static IEnumerable<Type> GetCombinedDependencies(
        IModule module,
        Dictionary<Type, IModule> map)
    {
        var attrDeps = module.GetType()
            .GetCustomAttributes<DependsOnAttribute>(inherit: true)
            .SelectMany(a => a.Dependencies);

        return attrDeps.Concat(module.DependsOn).Distinct();
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
            var module = _modules.First(
                m => m.GetType() == descriptor.ModuleType);
            await module.ShutdownAsync(ct);
        }
    }
}