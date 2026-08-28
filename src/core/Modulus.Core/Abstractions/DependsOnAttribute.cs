namespace Modulus.Core.Abstractions;

/// <summary>
/// Declares a module-level dependency on another <see cref="IModule"/>.
/// Apply one or more to a module class that inherits <see cref="ModulusModule"/>.
/// </summary>
/// <remarks>
/// The <see cref="ModulusModule"/> base class reads these attributes to
/// populate <see cref="IModule.DependsOn"/> automatically. The
/// <c>ModulusBuilder.AddModules&lt;TStartupModule&gt;</c> method scans them
/// recursively to discover and register the full module graph from a single
/// startup module.
/// </remarks>
/// <example>
/// <code>
/// [DependsOn(typeof(IdentityModule), typeof(DataModule))]
/// public sealed class ShopModule : ModulusModule { }
///
/// // Optional dependency: ordered first when present in the module set,
/// // silently ignored when absent — never pulled into the graph by discovery.
/// [DependsOn(typeof(TelemetryModule), Optional = true)]
/// public sealed class CatalogModule : ModulusModule { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DependsOnAttribute : Attribute
{
    /// <summary>The module types that must be initialised before this module.</summary>
    public Type[] Dependencies { get; }

    /// <summary>
    /// When <c>true</c>, the dependency is a soft dependency: modules already
    /// registered are initialised before this one, but an unregistered optional
    /// dependency does not fail startup and is not discovered transitively.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>Initialises the attribute with the specified module dependencies.</summary>
    /// <param name="dependencies">One or more <see cref="IModule"/> types.</param>
    public DependsOnAttribute(params Type[] dependencies)
        => Dependencies = dependencies;
}
