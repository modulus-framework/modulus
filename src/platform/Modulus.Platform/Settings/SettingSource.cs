namespace Modulus.Settings;

/// <summary>Where the effective value of a setting came from.</summary>
public enum SettingSource
{
    /// <summary>No value anywhere: no pin, no stored value, no default.</summary>
    None = 0,

    /// <summary>The definition's default.</summary>
    Default = 1,

    /// <summary>A value stored for the installation.</summary>
    Global = 2,

    /// <summary>A value stored for the current company.</summary>
    Tenant = 3,

    /// <summary>A value stored for the current person.</summary>
    User = 4,

    /// <summary>A value the host fixed in configuration. No scope overrides it.</summary>
    Pinned = 5,
}
