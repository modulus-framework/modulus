using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Configuration.Application.IntegrationEvents;

public sealed record SettingCreatedIntegrationEvent(
    Guid SettingId,
    string Key,
    string Category,
    bool IsPublic,
    Guid TenantId,
    DateTime CreatedAt) : IntegrationEventBase("Settings.SettingCreated.v1")
{
    public Guid SettingId { get; } = SettingId;
    public string Key { get; } = Key;
    public string Category { get; } = Category;
    public bool IsPublic { get; } = IsPublic;
    public Guid TenantId { get; } = TenantId;
    public DateTime CreatedAt { get; } = CreatedAt;
}
