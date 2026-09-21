namespace Modulus.UI.Users;

/// <summary>Permission names declared by the Users UI.</summary>
public static class UsersUiPermissions
{
    /// <summary>Administer users and roles.</summary>
    public const string Manage = "users:manage";
}

/// <summary>Options for the Users UI, bound from the <c>UsersUi</c> section.</summary>
public sealed class UsersUiOptions
{
    public const string SectionName = "UsersUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/Users</c> and
    /// <c>/Roles</c> folders. Defaults to <see cref="UsersUiPermissions.Manage"/>
    /// — every page model also carries a bare <c>[Authorize]</c> as an
    /// unconditional floor, so anonymous access is impossible even before
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention
    /// (without it, requests hit an unresolvable-policy error instead of
    /// silently serving admin pages — call <c>AddModulusAuthorization</c> to
    /// fix it). Set to <c>null</c> or <c>""</c> to opt out of the permission
    /// check and keep only the authentication floor.
    /// </summary>
    public string? RequirePermission { get; set; } = UsersUiPermissions.Manage;

    /// <summary>Maximum rows in the user/role browsers. Defaults to 50.</summary>
    public int ListLimit { get; set; } = 50;
}
