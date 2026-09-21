namespace Modulus.UI.Users;

using Modulus.Core.Abstractions;

/// <summary>
/// Navigation sidecar for the Users UI: user directory + role catalog over
/// <c>UserManager&lt;ModulusUser&gt;</c> / <c>RoleManager&lt;ModulusRole&gt;</c>.
/// Like the Identity UI this targets the base <c>ModulusUser</c> /
/// <c>ModulusRole</c> types directly (Razor Pages cannot close over a host's
/// custom <c>TUser : ModulusUser</c>); such hosts override these pages by
/// dropping same-route <c>.cshtml</c> files into their own project.
/// </summary>
public sealed class UsersUiModule : UiModule
{
    public override ModuleManifest Manifest { get; } = new(
        "Modulus.Users",
        "Users",
        "1.0.0",
        ["Modulus.Identity", "Modulus.UI.Core"],
        ["Users", "Roles"]);

    public override void ConfigureNavigation(UiNavigationBuilder navigation)
    {
        navigation.AddItem(
            "Users.Directory",
            "Users",
            "/users",
            groupId: "Administration",
            icon: "users",
            requiredPermission: UsersUiPermissions.Manage,
            order: 70);
        navigation.AddItem(
            "Users.Roles",
            "Roles",
            "/roles",
            groupId: "Administration",
            icon: "shield-lock",
            requiredPermission: UsersUiPermissions.Manage,
            order: 71);
    }
}
