using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Configuration.Application.IntegrationEvents;

public sealed record SettingDeletedIntegrationEvent(
    Guid SettingId,
    string Key,
    Guid TenantId,
    DateTime DeletedAt) : IntegrationEventBase("Settings.SettingDeleted.v1")
{
    public Guid SettingId { get; } = SettingId;
    public string Key { get; } = Key;
    public Guid TenantId { get; } = TenantId;
    public DateTime DeletedAt { get; } = DeletedAt;
}
