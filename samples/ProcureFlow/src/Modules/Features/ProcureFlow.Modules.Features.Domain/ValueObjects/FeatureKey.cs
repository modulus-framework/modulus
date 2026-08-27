using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Features.Domain.ValueObjects;

public sealed record FeatureKey
{
    private const int MaxLength = 256;

    public string Value { get; }

    private FeatureKey(string value)
    {
        Value = value;
    }

    public static Result<FeatureKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<FeatureKey>(Error.Validation("FeatureKey.Empty", "Feature key cannot be empty"));
        }

        if (value.Length > MaxLength)
        {
            return Result.Failure<FeatureKey>(Error.Validation("FeatureKey.TooLong", $"Feature key cannot exceed {MaxLength} characters"));
        }

        if (value.Any(char.IsWhiteSpace))
        {
            return Result.Failure<FeatureKey>(Error.Validation("FeatureKey.InvalidChars", "Feature key cannot contain whitespace characters"));
        }

        return Result.Success(new FeatureKey(value));
    }

    public static implicit operator string(FeatureKey featureKey) => featureKey.Value;
    public static FeatureKey FromString(string value) => new(value);
}
