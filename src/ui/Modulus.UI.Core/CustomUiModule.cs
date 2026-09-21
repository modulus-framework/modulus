namespace Modulus.UI;

/// <summary>
/// Ready-made <see cref="IUiModule"/> for app-specific module UIs. The framework
/// ships prebuilt UI modules only for its own areas (Users, Identity, Tenancy,
/// ...); every app-specific backend module (Catalog, Orders, <c>Foo</c>) gets
/// its UI sidecar by deriving from this class <b>in the app's own repo</b> —
/// no framework change, no copy-paste of the prebuilt-module internals.
/// </summary>
/// <example>
/// <code>
/// public sealed class CatalogUiModule : CustomUiModule
/// {
///     public CatalogUiModule()
///         : base(
///             new ModuleManifest(
///                 "MyApp.Catalog",
///                 "Catalog",
///                 "1.0.0",
///                 ["MyApp.Core"],
///                 ["Products", "Categories"]),
///             nav => nav
///                 .AddGroup("catalog", "Catalog", icon: "box", order: 30)
///                 .AddItem(
///                     "Catalog.Products",
///                     "Products",
///                     "/catalog/products",
///                     groupId: "catalog"))
///     {
///     }
/// }
///
/// // Program.cs
/// builder.Services.AddModulusUi(builder.Configuration);
/// builder.Services.AddUiModule&lt;CatalogUiModule&gt;();
/// </code>
/// </example>
public abstract class CustomUiModule : UiModule
{
    private readonly Action<UiNavigationBuilder>? _configure;

    /// <summary>
    /// Creates a custom UI module with a fixed manifest and an optional
    /// navigation contribution.
    /// </summary>
    protected CustomUiModule(ModuleManifest manifest, Action<UiNavigationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Manifest = manifest;
        _configure = configure;
    }

    /// <inheritdoc />
    public override ModuleManifest Manifest { get; }

    /// <inheritdoc />
    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _configure?.Invoke(navigation);
    }
}
