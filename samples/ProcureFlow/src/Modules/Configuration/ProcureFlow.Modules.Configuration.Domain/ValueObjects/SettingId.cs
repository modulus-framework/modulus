namespace ProcureFlow.Modules.Configuration.Domain.ValueObjects;

using System.Text.Json.Serialization;

public sealed record SettingId
{
    public Guid Value { get; }

    [JsonConstructor]
    private SettingId(Guid value)
    {
        Value = value;
    }

    public static SettingId Create() => new(Guid.NewGuid());
    public static SettingId From(Guid value) => new(value);

    public static implicit operator Guid(SettingId settingId) => settingId.Value;
    public static implicit operator SettingId(Guid value) => new(value);
}
