using Modulus.Events.Abstractions;
using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Domain.Events;

[IntegrationEventName("Settings.SettingCreated.v1")]
public sealed record SettingCreatedDomainEvent(
    SettingId SettingId,
    string Key,
    string Category,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Settings.SettingCreated.v1";
}
