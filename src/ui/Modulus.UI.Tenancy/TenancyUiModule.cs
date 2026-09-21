namespace Modulus.UI.Tenancy;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Tenancy UI. Tenants are a deployment/resolver
/// concern (header, subdomain, claim) — there is no in-app "switch tenant"
/// action — so the module contributes a directory entry under
/// <c>Administration</c> plus its manifest on <c>/_ui/modules</c>.
/// </summary>
public sealed class TenancyUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Tenancy",
        "Tenancy",
        "1.0.0",
        ["Modulus.Platform", "Modulus.UI.Core"],
        ["Tenancy"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "Tenancy.Directory",
            "Tenants",
            "/tenancy",
            groupId: "Administration",
            icon: "building",
            requiredPermission: TenancyUiPermissions.View,
            order: 10);
    }
}
