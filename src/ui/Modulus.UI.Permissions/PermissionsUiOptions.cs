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
    /// folder. Defaults to <c>null</c> (pages open) so the UI works without
    /// the authorization stack; set to
    /// <see cref="PermissionsUiPermissions.View"/> once
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention.
    /// Declared on the registry by <c>AddModulusPermissionsUi</c>.
    /// </summary>
    public string? RequirePermission { get; set; }
}
