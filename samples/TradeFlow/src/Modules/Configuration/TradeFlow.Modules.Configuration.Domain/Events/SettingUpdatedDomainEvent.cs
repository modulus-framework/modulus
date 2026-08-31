using Modulus.Events.Abstractions;
using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Domain.Events;

[IntegrationEventName("Settings.SettingUpdated.v1")]
public sealed record SettingUpdatedDomainEvent(
    SettingId SettingId,
    string Key,
    string OldValue,
    string NewValue,
    string ModifiedBy,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Settings.SettingUpdated.v1";
}
