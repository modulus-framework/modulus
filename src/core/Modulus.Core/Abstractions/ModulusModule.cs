namespace Modulus.Core.Abstractions;

using System.Collections.Concurrent;

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
/// Overrides are unioned with (not replace) attribute-declared dependencies —
/// declare optional ones via <c>[DependsOn(..., Optional = true)]</c>.
/// </remarks>
public abstract class ModulusModule : IModule
{
    // Module types are static for the process lifetime, so the reflected
    // [DependsOn] result can be cached — startup reads this repeatedly.
    private static readonly ConcurrentDictionary<Type, Type[]> DependsOnCache = new();

    /// <summary>
    /// Resolved from non-optional <see cref="DependsOnAttribute"/>s on the
    /// derived type (cached per concrete type — attributes are read via
    /// reflection once). Optional dependencies (<c>Optional = true</c>) are
    /// excluded here; they are handled by the graph engine directly.
    /// Override to provide additional dependencies programmatically.
    /// </summary>
    public virtual Type[] DependsOn =>
        DependsOnCache.GetOrAdd(
            GetType(),
            static t => t
                .GetCustomAttributes(typeof(DependsOnAttribute), inherit: true)
                .Cast<DependsOnAttribute>()
                .Where(a => !a.Optional)
                .SelectMany(a => a.Dependencies)
                .Distinct()
                .ToArray());

    /// <inheritdoc />
    public virtual void PreConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    { }

    /// <inheritdoc />
    public virtual void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    { }

    /// <inheritdoc />
    public virtual void PostConfigureServices(
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
