namespace Modulus.UI.Permissions;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Permissions UI: the permission catalog and the
/// holder-grant viewer. Grant <em>editing</em> stays in the
/// <c>/authorization</c> management REST API (SoD simulation, audit) — these
/// pages are the read surface over <see cref="IPermissionRegistry"/> and the
/// grant store.
/// </summary>
public sealed class PermissionsUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Permissions",
        "Permissions",
        "1.0.0",
        ["Modulus.Platform", "Modulus.UI.Core"],
        ["Permissions"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "Permissions.Catalog",
            "Permissions",
            "/permissions",
            groupId: "Administration",
            icon: "lock",
            requiredPermission: PermissionsUiPermissions.View,
            order: 20);
        navigation.AddItem(
            "Permissions.Holder",
            "Grants by holder",
            "/permissions/holder",
            groupId: "Administration",
            icon: "key",
            requiredPermission: PermissionsUiPermissions.View,
            order: 21);
    }
}
