using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Configuration.Domain.ValueObjects;

namespace ProcureFlow.Modules.Configuration.Domain.Events;

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
