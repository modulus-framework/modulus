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
    /// <c>/Roles</c> folders. Defaults to <c>null</c> (pages open) so the UI
    /// works without the authorization stack; set to
    /// <see cref="UsersUiPermissions.Manage"/> once
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention.
    /// Declared on the registry by <c>AddModulusUsersUi</c>.
    /// </summary>
    public string? RequirePermission { get; set; }

    /// <summary>Maximum rows in the user/role browsers. Defaults to 50.</summary>
    public int ListLimit { get; set; } = 50;
}
