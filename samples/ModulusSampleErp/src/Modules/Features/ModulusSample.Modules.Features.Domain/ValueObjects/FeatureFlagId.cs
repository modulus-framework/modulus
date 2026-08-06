namespace ModulusSample.Modules.Features.Domain.ValueObjects;

public sealed record FeatureFlagId
{
    public Guid Value { get; }

    private FeatureFlagId(Guid value)
    {
        Value = value;
    }

    public static FeatureFlagId Create() => new(Guid.NewGuid());
    public static FeatureFlagId From(Guid value) => new(value);

    public static implicit operator Guid(FeatureFlagId featureFlagId) => featureFlagId.Value;
    public static implicit operator FeatureFlagId(Guid value) => new(value);
}