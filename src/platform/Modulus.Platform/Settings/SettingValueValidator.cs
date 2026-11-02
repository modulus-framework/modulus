namespace Modulus.Settings;

using System.Globalization;

/// <summary>Checks a value against its definition before it is stored.</summary>
internal static class SettingValueValidator
{
    /// <summary>The longest value the store accepts.</summary>
    public const int MaxValueLength = 4000;

    private static readonly System.Text.RegularExpressions.Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ColorPattern =
        new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
            SettingValueType.Url => IsAbsoluteHttpUrl(value),
            SettingValueType.Email => EmailPattern.IsMatch(value) && value.Length <= 254,
            SettingValueType.Color => ColorPattern.IsMatch(value),
            _ => true,
        };

        return valid ? null : $"'{definition.Name}' must be a {DescribeType(definition.ValueType)} value.";
    }

    private static bool IsAbsoluteHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && uri.UserInfo.Length == 0;

    private static string DescribeType(SettingValueType type) => type switch
    {
        SettingValueType.Url => "absolute http or https URL",
        SettingValueType.Email => "email address",
        SettingValueType.Color => "hex colour (#rgb or #rrggbb)",
        _ => type.ToString(),
    };
}
