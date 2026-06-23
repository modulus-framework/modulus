namespace Modulus.Core;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;

public sealed class ModuleLoader : IModuleLoader
{
    private IReadOnlyList<ModuleDescriptor> _sorted = [];
    private IReadOnlyList<IModule>          _modules = [];

    // ── BuildGraph ────────────────────────────────────────────────
    public IReadOnlyList<ModuleDescriptor> BuildGraph(
        IEnumerable<IModule> modules)
    {
        _modules = modules.ToList();
        var map     = _modules.ToDictionary(m => m.GetType());
        var visited = new HashSet<Type>();
        var inStack = new HashSet<Type>();
        var sorted  = new List<ModuleDescriptor>();
        int order   = 0;

        void Visit(IModule module)
        {
            var t = module.GetType();
            if (inStack.Contains(t))
                throw new CircularDependencyException(t);
            if (visited.Contains(t)) return;

            inStack.Add(t);
            foreach (var dep in module.DependsOn)
            {
                if (!map.TryGetValue(dep, out var depModule))
                    throw new ModuleNotFoundException(dep);
                Visit(depModule);
            }
            inStack.Remove(t);
            visited.Add(t);
            sorted.Add(new ModuleDescriptor
            {
                Name         = t.Name,
                ModuleType   = t,
                Dependencies = module.DependsOn,
                InitOrder    = order++,
            });
        }

        foreach (var m in _modules) Visit(m);
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
            var sw     = System.Diagnostics.Stopwatch.StartNew();

            var ctx = new ModuleContext
            {
                ServiceProvider = sp,
                Configuration   = sp.GetRequiredService<IConfiguration>(),
                Logger = sp.GetRequiredService<ILoggerFactory>()
                           .CreateLogger(descriptor.ModuleType),
                Descriptor      = descriptor,
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