namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>The setting change counter row (<c>ModulusSettingVersions</c>). Each write to a setting advances <see cref="Version"/>.</summary>
public sealed class SettingVersionEntity
{
    /// <summary>The name of the one row that counts the setting changes.</summary>
    public const string SettingsName = "settings";

    /// <summary>The counter's name; always <see cref="SettingsName"/>.</summary>
    public string Name { get; set; } = SettingsName;

    /// <summary>How many setting changes the store has made.</summary>
    public long Version { get; set; }
}
