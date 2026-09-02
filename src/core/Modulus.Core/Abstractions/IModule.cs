namespace Modulus.Core.Abstractions;

/// <summary>
/// Primary contract for every Modulus module. Implement once per feature area
/// (or inherit from <see cref="ModulusModule"/> for no-op defaults) and
/// register it explicitly via
/// <c>ModulusBuilder.AddModule&lt;TModule&gt;()</c> inside
/// <c>AddModulus(configuration, modules => ...)</c>.
/// </summary>
/// <remarks>
/// Modules have no dependency declarations: the order in which modules are
/// registered is the order in which every lifecycle phase runs —
/// <see cref="PreConfigureServices"/>, <see cref="ConfigureServices"/>,
/// <see cref="PostConfigureServices"/>, and <see cref="InitializeAsync"/>.
/// <see cref="ShutdownAsync"/> runs in reverse registration order. Register a
/// module before the modules that rely on its services.
/// </remarks>
public interface IModule
{
    /// <summary>
    /// Runs for <b>every</b> module before <b>any</b> module's
    /// <see cref="ConfigureServices"/>. Use it to register cross-cutting services
    /// or seed shared options/registries that other modules contribute to while
    /// they configure (e.g. an options object a later module mutates). Runs in
    /// registration order.
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
    /// in registration order.
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
