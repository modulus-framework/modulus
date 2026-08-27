using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Tenants.Domain.Events;

[IntegrationEventName("Tenants.TenantCreated.v1")]
public sealed record TenantCreatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string Subdomain,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantCreated.v1";
}

[IntegrationEventName("Tenants.TenantUpdated.v1")]
public sealed record TenantUpdatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantUpdated.v1";
}

[IntegrationEventName("Tenants.TenantActivated.v1")]
public sealed record TenantActivatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantActivated.v1";
}

[IntegrationEventName("Tenants.TenantDeactivated.v1")]
public sealed record TenantDeactivatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantDeactivated.v1";
}

[IntegrationEventName("Tenants.TenantDeleted.v1")]
public sealed record TenantDeletedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string DeletedBy,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantDeleted.v1";
}

[IntegrationEventName("Tenants.TenantFeaturesUpdated.v1")]
public sealed record TenantFeaturesUpdatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantFeaturesUpdated.v1";
}

[IntegrationEventName("Tenants.TenantSettingsUpdated.v1")]
public sealed record TenantSettingsUpdatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string Name,
    string ModifiedBy,
    DateTime OccurredAt) : IDomainEvent, IIntegrationEvent
{
    public string EventType => "Tenants.TenantSettingsUpdated.v1";
}
