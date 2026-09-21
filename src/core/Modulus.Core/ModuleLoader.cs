namespace Modulus.Core;

using Modulus.Core.Abstractions;
using Modulus.Observability;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Thrown when a module's <see cref="IModule.InitializeAsync"/> fails. Carries
/// the module name so boot failures are attributable instead of surfacing as a
/// bare handler exception. Remaining modules are not initialized.
/// </summary>
public sealed class ModuleInitializationException(string moduleName, Exception inner)
    : InvalidOperationException($"Module '{moduleName}' failed to initialize.", inner)
{
    public string ModuleName { get; } = moduleName;
}

/// <summary>
/// Default <see cref="IModuleLoader"/>: captures the registered modules in
/// registration order, initializes them in that order, and shuts them down in
/// reverse. Built by <see cref="ModulusBuilder.Complete"/>.
/// </summary>
public sealed class ModuleLoader : IModuleLoader
{
    private readonly IReadOnlyList<ModuleDescriptor> _descriptors;
    private readonly Dictionary<Type, IModule> _modulesByType;
    private readonly HashSet<Type> _initialized = new();

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
            // Per-module child scope so scoped services (DbContext, tenant,
            // correlation) never bleed across modules during init.
            await using var scope = sp.CreateAsyncScope();
            var scoped = scope.ServiceProvider;
            var module = (IModule)scoped.GetRequiredService(descriptor.ModuleType);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var ctx = new ModuleContext
            {
                ServiceProvider = scoped,
                Configuration = scoped.GetRequiredService<IConfiguration>(),
                Logger = scoped.GetRequiredService<ILoggerFactory>()
                           .CreateLogger(descriptor.ModuleType),
                Descriptor = descriptor,
            };

            try
            {
                await module.InitializeAsync(ctx, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sw.Stop();
                logger.LogCritical(ex,
                    "[Modulus] {Module} failed to initialize; aborting remaining initializations.",
                    descriptor.Name);
                throw new ModuleInitializationException(descriptor.Name, ex);
            }

            sw.Stop();
            lock (_initialized)
                _initialized.Add(descriptor.ModuleType);

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
    public async Task ShutdownAllAsync(
        IServiceProvider sp,
        CancellationToken ct = default)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ModuleLoader>();

        for (var i = _descriptors.Count - 1; i >= 0; i--)
        {
            var descriptor = _descriptors[i];
            lock (_initialized)
            {
                // After a failed boot only shut down modules that initialized;
                // when init never ran, shut everything down (safe no-op path).
                if (_initialized.Count > 0 && !_initialized.Contains(descriptor.ModuleType))
                    continue;
            }
            var module = (IModule?)sp.GetService(descriptor.ModuleType)
                ?? _modulesByType[descriptor.ModuleType];

            try
            {
                await module.ShutdownAsync(ct);
            }
            catch (Exception ex)
            {
                // Log and continue: a broken module's ShutdownAsync must not
                // abort the loop, or every module still queued (earlier in
                // registration order, later in shutdown order) never gets its
                // own ShutdownAsync called — leaking connections and dropping
                // in-flight work on a shutdown that's already underway.
                logger.LogError(ex,
                    "[Modulus] {Module} threw during shutdown; continuing with remaining modules.",
                    descriptor.Name);
            }
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
