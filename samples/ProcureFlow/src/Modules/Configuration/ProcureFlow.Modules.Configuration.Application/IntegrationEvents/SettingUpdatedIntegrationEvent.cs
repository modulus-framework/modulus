using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Configuration.Application.IntegrationEvents;

public sealed record SettingUpdatedIntegrationEvent(
    Guid SettingId,
    string Key,
    string OldValue,
    string NewValue,
    Guid TenantId,
    DateTime UpdatedAt) : IntegrationEventBase("Settings.SettingUpdated.v1")
{
    public Guid SettingId { get; } = SettingId;
    public string Key { get; } = Key;
    public string OldValue { get; } = OldValue;
    public string NewValue { get; } = NewValue;
    public Guid TenantId { get; } = TenantId;
    public DateTime UpdatedAt { get; } = UpdatedAt;
}
