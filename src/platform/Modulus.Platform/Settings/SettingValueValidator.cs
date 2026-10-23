namespace Modulus.Settings;

using System.Globalization;

/// <summary>Checks a value against its definition before it is stored.</summary>
internal static class SettingValueValidator
{
    /// <summary>The longest value the store accepts.</summary>
    public const int MaxValueLength = 4000;

    /// <summary>Returns why <paramref name="value"/> is not acceptable for <paramref name="definition"/>, or <see langword="null"/> when it is.</summary>
    public static string? Validate(SettingDefinition definition, string value)
    {
        if (definition.AllowedValues.Count > 0 && !definition.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            return $"'{definition.Name}' must be one of: {string.Join(", ", definition.AllowedValues)}.";

        var valid = definition.ValueType switch
        {
            SettingValueType.Boolean => bool.TryParse(value, out _),
            SettingValueType.Int32 => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            SettingValueType.Int64 => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            SettingValueType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            SettingValueType.Guid => Guid.TryParse(value, out _),
            SettingValueType.TimeSpan => TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out _),
            _ => true,
        };

        return valid ? null : $"'{definition.Name}' must be a {definition.ValueType} value.";
    }
}
