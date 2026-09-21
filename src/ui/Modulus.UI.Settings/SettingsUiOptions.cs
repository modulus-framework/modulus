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
    /// folder. Defaults to <c>null</c> (pages open) so the UI works without
    /// the authorization stack; set to
    /// <see cref="SettingsUiPermissions.Manage"/> once
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention.
    /// Declared on the registry by <c>AddModulusSettingsUi</c>.
    /// </summary>
    public string? RequirePermission { get; set; }
}
