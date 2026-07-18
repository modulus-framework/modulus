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
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DependsOnAttribute : Attribute
{
    /// <summary>The module types that must be initialised before this module.</summary>
    public Type[] Dependencies { get; }

    /// <summary>Initialises the attribute with the specified module dependencies.</summary>
    /// <param name="dependencies">One or more <see cref="IModule"/> types.</param>
    public DependsOnAttribute(params Type[] dependencies)
        => Dependencies = dependencies;
}
