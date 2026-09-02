namespace Modulus.Core.Abstractions;

/// <summary>
/// Convenience base class for Modulus modules. Provides no-op defaults so
/// derived classes override only the members they need.
/// </summary>
/// <remarks>
/// Modules declare no dependencies; lifecycle order is the registration order
/// chosen in <c>AddModulus(configuration, modules => ...)</c>. Register a
/// module before the modules that rely on its services.
/// <code>
/// public sealed class CatalogModule : ModulusModule
/// {
///     public override void ConfigureServices(IServiceCollection s, IConfiguration c)
///     {
///         // register services ...
///     }
/// }
/// </code>
/// </remarks>
public abstract class ModulusModule : IModule
{
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
