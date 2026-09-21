namespace Modulus.UI.Permissions;

/// <summary>Permission names declared by the Permissions UI.</summary>
public static class PermissionsUiPermissions
{
    /// <summary>View the permission catalog and holder-grant pages.</summary>
    public const string View = "permissions:view";
}

/// <summary>Options for the Permissions UI, bound from the <c>PermissionsUi</c> section.</summary>
public sealed class PermissionsUiOptions
{
    public const string SectionName = "PermissionsUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/Permissions</c>
    /// folder. Defaults to <see cref="PermissionsUiPermissions.View"/> —
    /// every page model also carries a bare <c>[Authorize]</c> as an
    /// unconditional floor, so anonymous access is impossible even before
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention
    /// (without it, requests hit an unresolvable-policy error instead of
    /// silently serving admin pages — call <c>AddModulusAuthorization</c> to
    /// fix it). Set to <c>null</c> or <c>""</c> to opt out of the permission
    /// check and keep only the authentication floor.
    /// </summary>
    public string? RequirePermission { get; set; } = PermissionsUiPermissions.View;
}
