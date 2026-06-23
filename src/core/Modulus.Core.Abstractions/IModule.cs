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
    /// Register all DI services for this module.
    /// Called during host startup before app.Build().
    /// </summary>
    void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration);

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