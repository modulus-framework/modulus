namespace TradeFlow.Modules.Configuration.Domain.ValueObjects;

using System.Text.Json.Serialization;

public sealed record FeatureFlagId
{
    public Guid Value { get; }

    [JsonConstructor]
    private FeatureFlagId(Guid value)
    {
        Value = value;
    }

    public static FeatureFlagId Create() => new(Guid.NewGuid());
    public static FeatureFlagId From(Guid value) => new(value);

    public static implicit operator Guid(FeatureFlagId featureFlagId) => featureFlagId.Value;
    public static implicit operator FeatureFlagId(Guid value) => new(value);
}
