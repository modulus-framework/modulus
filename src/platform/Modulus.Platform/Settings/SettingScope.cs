namespace Modulus.Settings;

/// <summary>Value scope a setting applies to. Narrower scopes win.</summary>
public enum SettingScope
{
    /// <summary>Shared by the whole installation (host context).</summary>
    Global = 0,

    /// <summary>Overrides <see cref="Global"/> for one tenant.</summary>
    Tenant = 1,

    /// <summary>Overrides <see cref="Tenant"/> for one user.</summary>
    User = 2,
}
