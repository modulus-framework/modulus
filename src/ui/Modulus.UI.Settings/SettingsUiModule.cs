namespace Modulus.UI.Settings;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Settings UI: browser and editor over the
/// setting-definition registry and the scoped setting manager.
/// </summary>
public sealed class SettingsUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Settings",
        "Settings",
        "1.0.0",
        ["Modulus.Platform", "Modulus.UI.Core"],
        ["Settings"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "Settings.Browser",
            "Settings",
            "/settings",
            groupId: "Administration",
            icon: "settings",
            requiredPermission: SettingsUiPermissions.Manage,
            order: 30);
    }
}
