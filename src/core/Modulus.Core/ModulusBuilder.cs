namespace Modulus.Core;

using Modulus.Core.Abstractions;

/// <summary>
/// Fluent builder used inside <c>AddModulus(...)</c>. Modules are registered
/// explicitly via <see cref="AddModule{TModule}"/> — there is no discovery and
/// no dependency declaration; the registration order chosen here is the order
/// every lifecycle phase runs in.
/// </summary>
/// <remarks>
/// Every module type is instantiated <b>exactly once</b>. The single instance is
/// used for the three configuration phases, is the same object registered in DI,
/// and is later initialized by <c>ModuleLoader</c>.
/// <para>
/// Service configuration runs in three ordered phases inside <see cref="Complete"/>
/// — <see cref="IModule.PreConfigureServices"/> for every module, then
/// <see cref="IModule.ConfigureServices"/> for every module, then
/// <see cref="IModule.PostConfigureServices"/> for every module — each phase in
/// registration order. Registration is therefore deferred until
/// <see cref="Complete"/> (always called by <c>AddModulus(...)</c>), which lets a
/// module in an earlier phase set up state that a later module refines.
/// </para>
/// </remarks>
public sealed class ModulusBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    // Single instance per module type. Shared by registration and configuration.
    private readonly Dictionary<Type, IModule> _instances = [];

    // Registered modules in registration order.
    internal List<IModule> Modules { get; } = [];

    public ModulusBuilder(
        IServiceCollection services,
        IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    /// <summary>
    /// Register a single module. The module's configuration phases run later,
    /// in <see cref="Complete"/>, batched with every other module in
    /// registration order.
    /// </summary>
    public ModulusBuilder AddModule<TModule>()
        where TModule : class, IModule, new()
        => AddModule(typeof(TModule));

    /// <summary>
    /// Register a single module by type. Instantiates via parameterless
    /// constructor (once) and adds the instance to DI. No-op if a module of the
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

        if (moduleType.IsAbstract)
            throw new InvalidOperationException(
                $"{moduleType.FullName} is abstract and cannot be instantiated. " +
                "Register a concrete module type.");

        // Skip if already registered (idempotent registration).
        if (Modules.Any(m => m.GetType() == moduleType))
            return this;

        var module = GetOrCreate(moduleType);
        Modules.Add(module);

        _services.AddSingleton(typeof(IModule), module);
        _services.AddSingleton(moduleType, module);
        return this;
    }

    /// <summary>
    /// Runs the three service-configuration phases across every registered
    /// module (each in registration order), then builds the
    /// <see cref="IModuleLoader"/> and registers it as a singleton. Called by
    /// <c>AddModulus(...)</c> after all modules are registered, so the module
    /// list is ready before the host starts — an app that forgets
    /// <c>UseModulus()</c> still initializes its modules.
    /// </summary>
    public IModuleLoader Complete()
    {
        foreach (var module in Modules)
            module.PreConfigureServices(_services, _configuration);

        foreach (var module in Modules)
            module.ConfigureServices(_services, _configuration);

        foreach (var module in Modules)
            module.PostConfigureServices(_services, _configuration);

        var loader = new ModuleLoader(Modules);
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
}
