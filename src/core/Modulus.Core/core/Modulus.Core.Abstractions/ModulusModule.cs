namespace Modulus.Core.Abstractions;

/// <summary>
/// Convenience base class for Modulus modules.  Provides no-op defaults so
/// derived classes override only the members they need.
/// </summary>
/// <remarks>
/// When you inherit from <see cref="ModulusModule"/> you can declare
/// dependencies declaratively via <see cref="DependsOnAttribute"/> instead of
/// implementing <see cref="IModule.DependsOn"/> manually:
/// <code>
/// [DependsOn(typeof(IdentityModule))]
/// public sealed class CatalogModule : ModulusModule
/// {
///     public override void ConfigureServices(IServiceCollection s, IConfiguration c)
///     {
///         // register services ...
///     }
/// }
/// </code>
/// You can still override <see cref="DependsOn"/> if you need full control.
/// </remarks>
public abstract class ModulusModule : IModule
{
    /// <summary>
    /// Resolved from <see cref="DependsOnAttribute"/>s on the derived type.
    /// Override to provide dependencies programmatically.
    /// </summary>
    public virtual Type[] DependsOn =>
        GetType()
            .GetCustomAttributes(typeof(DependsOnAttribute), inherit: true)
            .Cast<DependsOnAttribute>()
            .SelectMany(a => a.Dependencies)
            .Distinct()
            .ToArray();

    /// <inheritdoc />
    public virtual void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    { }

    /// <inheritdoc />
    public virtual Task InitializeAsync(
        ModuleContext context,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task ShutdownAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
