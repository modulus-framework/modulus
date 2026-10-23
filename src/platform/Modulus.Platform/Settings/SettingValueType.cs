namespace Modulus.Settings;

/// <summary>What a setting's value must parse as. Values are stored as text; the type is checked when a value is written.</summary>
public enum SettingValueType
{
    /// <summary>Any text (up to 4000 characters), or one of <see cref="SettingDefinition.AllowedValues"/>.</summary>
    String = 0,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean = 1,

    /// <summary>A 32-bit integer.</summary>
    Int32 = 2,

    /// <summary>A 64-bit integer.</summary>
    Int64 = 3,

    /// <summary>A decimal number, in invariant culture.</summary>
    Decimal = 4,

    /// <summary>A GUID.</summary>
    Guid = 5,

    /// <summary>A time span, in the invariant <c>[d.]hh:mm[:ss]</c> form.</summary>
    TimeSpan = 6,
}
