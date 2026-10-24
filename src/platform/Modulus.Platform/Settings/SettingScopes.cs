namespace Modulus.Settings;

/// <summary>The set of scopes a setting may hold a value at. A definition names the ones it allows.</summary>
[Flags]
public enum SettingScopes
{
    /// <summary>No scope: the setting can only be read from its default or a pin.</summary>
    None = 0,

    /// <summary>The installation-wide value.</summary>
    Global = 1 << (int)SettingScope.Global,

    /// <summary>A company's value.</summary>
    Tenant = 1 << (int)SettingScope.Tenant,

    /// <summary>A person's value.</summary>
    User = 1 << (int)SettingScope.User,

    /// <summary>A branch's value (an organization unit of the company).</summary>
    Branch = 1 << (int)SettingScope.Branch,

    /// <summary>Every scope.</summary>
    All = Global | Tenant | User | Branch,
}
