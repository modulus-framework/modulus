using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Domain.ValueObjects;

public sealed record SettingKey
{
    private const int MaxLength = 256;
    private static readonly HashSet<char> InvalidChars = [' ', '\t', '\n', '\r'];

    public string Value { get; }

    private SettingKey(string value)
    {
        Value = value;
    }

    public static Result<SettingKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<SettingKey>(Error.Validation("SettingKey.Empty", "Setting key cannot be empty"));
        }

        if (value.Length > MaxLength)
        {
            return Result.Failure<SettingKey>(Error.Validation("SettingKey.TooLong", $"Setting key cannot exceed {MaxLength} characters"));
        }

        if (value.Any(InvalidChars.Contains))
        {
            return Result.Failure<SettingKey>(Error.Validation("SettingKey.InvalidChars", "Setting key cannot contain whitespace characters"));
        }

        return Result.Success(new SettingKey(value));
    }

    public static implicit operator string(SettingKey settingKey) => settingKey.Value;
    public static SettingKey FromString(string value) => new(value);
}