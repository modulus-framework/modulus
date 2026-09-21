namespace Modulus.UI.Settings;

/// <summary>Permission names declared by the Settings UI.</summary>
public static class SettingsUiPermissions
{
    /// <summary>View and edit settings.</summary>
    public const string Manage = "settings:manage";
}

/// <summary>Options for the Settings UI, bound from the <c>SettingsUi</c> section.</summary>
public sealed class SettingsUiOptions
{
    public const string SectionName = "SettingsUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/Settings</c>
    /// folder. Defaults to <see cref="SettingsUiPermissions.Manage"/> — every
    /// page model also carries a bare <c>[Authorize]</c> as an unconditional
    /// floor, so anonymous access is impossible even before
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention
    /// (without it, requests hit an unresolvable-policy error instead of
    /// silently serving admin pages — call <c>AddModulusAuthorization</c> to
    /// fix it). Set to <c>null</c> or <c>""</c> to opt out of the permission
    /// check and keep only the authentication floor.
    /// </summary>
    public string? RequirePermission { get; set; } = SettingsUiPermissions.Manage;
}
