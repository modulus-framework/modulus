namespace Modulus.Core;

using Modulus.Core.Abstractions;
using Modulus.Observability;

/// <summary>
/// Default <see cref="IModuleLoader"/>: captures the registered modules in
/// registration order, initializes them in that order, and shuts them down in
/// reverse. Built by <see cref="ModulusBuilder.Complete"/>.
/// </summary>
public sealed class ModuleLoader : IModuleLoader
{
    private readonly IReadOnlyList<ModuleDescriptor> _descriptors;
    private readonly Dictionary<Type, IModule> _modulesByType;

    /// <summary>
    /// Creates a loader over the given modules. Registration order is
    /// authoritative: it becomes the configuration-phase order, the init
    /// order (<see cref="InitializeAllAsync"/>), and the reverse of the
    /// shutdown order (<see cref="ShutdownAllAsync"/>).
    /// </summary>
    public ModuleLoader(IEnumerable<IModule> modules)
    {
        var list = modules.ToList();
        _modulesByType = BuildModuleMap(list);

        var descriptors = new List<ModuleDescriptor>(list.Count);
        var order = 0;
        foreach (var module in list)
        {
            var type = module.GetType();
            descriptors.Add(new ModuleDescriptor
            {
                Name = type.Name,
                ModuleType = type,
                InitOrder = order++,
            });
        }

        _descriptors = descriptors.AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ModuleDescriptor> GetDescriptors() => _descriptors;

    // ── InitializeAllAsync ────────────────────────────────────────
    public async Task InitializeAllAsync(
        IServiceProvider sp,
        CancellationToken ct = default)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ModuleLoader>();

        logger.LogInformation("[Modulus] Initializing {Count} modules...",
            _descriptors.Count);

        foreach (var descriptor in _descriptors)
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

            ModulusMeters.ModuleInitDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("module", descriptor.Name));

            logger.LogInformation(
                "[Modulus] {Module} initialized ({Ms}ms)",
                descriptor.Name, sw.ElapsedMilliseconds);
        }

        logger.LogInformation("[Modulus] All {Count} modules ready.",
            _descriptors.Count);
    }

    // ── ShutdownAllAsync ──────────────────────────────────────────
    public async Task ShutdownAllAsync(CancellationToken ct = default)
    {
        for (var i = _descriptors.Count - 1; i >= 0; i--)
        {
            var descriptor = _descriptors[i];
            var module = _modulesByType[descriptor.ModuleType];
            await module.ShutdownAsync(ct);
        }
    }

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
                    "Each module type may only appear once in the module list.");
        }

        return map;
    }
}
