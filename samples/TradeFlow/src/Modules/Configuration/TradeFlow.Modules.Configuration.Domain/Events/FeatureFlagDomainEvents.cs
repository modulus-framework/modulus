using Modulus.Events.Abstractions;
using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Domain.Events;

[IntegrationEventName("Features.FeatureFlagCreated.v1")]
public sealed record FeatureFlagCreatedDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    string Name,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Features.FeatureFlagCreated.v1";
}

[IntegrationEventName("Features.FeatureFlagUpdated.v1")]
public sealed record FeatureFlagUpdatedDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    string Name,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Features.FeatureFlagUpdated.v1";
}

[IntegrationEventName("Features.FeatureFlagDeleted.v1")]
public sealed record FeatureFlagDeletedDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Features.FeatureFlagDeleted.v1";
}

[IntegrationEventName("Features.FeatureFlagToggled.v1")]
public sealed record FeatureFlagToggledDomainEvent(
    FeatureFlagId FeatureFlagId,
    string Key,
    bool IsEnabled,
    Guid TenantId,
    DateTime OccurredAtUtc) : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
{
    public string EventType => "Features.FeatureFlagToggled.v1";
}
