namespace Modulus.Core.Abstractions;

/// <summary>
/// Primary contract for every Modulus module.
/// Implement once per feature area. Discovered by ModuleLoader at startup.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Other IModule types this module requires.
    /// ModuleLoader validates these and initialises dependencies first.
    /// </summary>
    Type[] DependsOn { get; }

    /// <summary>
    /// Runs for <b>every</b> module before <b>any</b> module's
    /// <see cref="ConfigureServices"/>. Use it to register cross-cutting services
    /// or seed shared options/registries that other modules contribute to while
    /// they configure (e.g. an options object a later module mutates). Runs in
    /// dependency order (dependencies first).
    /// </summary>
    /// <remarks>Default implementation is a no-op.</remarks>
    void PreConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    { }

    /// <summary>
    /// Register all DI services for this module.
    /// Called during host startup before app.Build().
    /// </summary>
    void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration);

    /// <summary>
    /// Runs for <b>every</b> module after <b>all</b> modules'
    /// <see cref="ConfigureServices"/>. Use it to inspect the fully-populated
    /// service collection and finalize — freeze registries, build consolidated
    /// maps, or apply decorators that must see every module's registrations. Runs
    /// in dependency order (dependencies first).
    /// </summary>
    /// <remarks>Default implementation is a no-op.</remarks>
    void PostConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    { }

    /// <summary>
    /// Called after all modules have registered services.
    /// Run DB migrations, seed data, start background processes.
    /// </summary>
    Task InitializeAsync(
        ModuleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Called on graceful application shutdown.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}