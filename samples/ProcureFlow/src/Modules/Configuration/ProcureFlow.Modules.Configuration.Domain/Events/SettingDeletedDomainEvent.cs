using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Configuration.Domain.ValueObjects;

namespace ProcureFlow.Modules.Configuration.Domain.Events;

[IntegrationEventName("Settings.SettingDeleted.v1")]
public sealed record SettingDeletedDomainEvent(
    SettingId SettingId,
    string Key,
    string Value,
    string DeletedBy,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Settings.SettingDeleted.v1";
}
