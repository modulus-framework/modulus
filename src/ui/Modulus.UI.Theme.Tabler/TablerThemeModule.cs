namespace Modulus.UI.Theming.Tabler;

/// <summary>
/// Advertises the Tabler theme on <c>/_ui/modules</c>. The theme contributes
/// no navigation; the module exists so the CLI and manifest endpoint can see
/// which theme package an app installed.
/// </summary>
public sealed class TablerThemeModule : UiModule
{
    /// <inheritdoc />
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.UI.Theme.Tabler",
        "Tabler Theme",
        "1.4.0",
        ["Modulus.UI.Core"],
        ["Layouts", "Shell", "Error pages", "Design tokens", "Client runtime"]);
}
