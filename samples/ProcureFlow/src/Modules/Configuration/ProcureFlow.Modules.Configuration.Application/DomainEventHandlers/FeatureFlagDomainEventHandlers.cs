using Modulus.Events.Abstractions;
using ProcureFlow.Modules.Configuration.Application.IntegrationEvents;
using ProcureFlow.Modules.Configuration.Domain.Events;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Modules.Configuration.Application.DomainEventHandlers;

public sealed class FeatureFlagCreatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<FeatureFlagCreatedDomainEventHandler> logger) : IDomainEventHandler<FeatureFlagCreatedDomainEvent>
{
    public Task HandleAsync(FeatureFlagCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing FeatureFlagCreatedIntegrationEvent - Key: {Key}", @event.Key);

        return moduleBus.PublishAsync(new FeatureFlagCreatedIntegrationEvent(
            @event.FeatureFlagId.Value,
            @event.Key,
            @event.Name,
            @event.TenantId,
            @event.OccurredAtUtc), ct);
    }
}

public sealed class FeatureFlagUpdatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<FeatureFlagUpdatedDomainEventHandler> logger) : IDomainEventHandler<FeatureFlagUpdatedDomainEvent>
{
    public Task HandleAsync(FeatureFlagUpdatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing FeatureFlagUpdatedIntegrationEvent - Key: {Key}", @event.Key);

        return moduleBus.PublishAsync(new FeatureFlagUpdatedIntegrationEvent(
            @event.FeatureFlagId.Value,
            @event.Key,
            @event.Name,
            false,
            @event.TenantId,
            @event.OccurredAtUtc), ct);
    }
}

public sealed class FeatureFlagDeletedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<FeatureFlagDeletedDomainEventHandler> logger) : IDomainEventHandler<FeatureFlagDeletedDomainEvent>
{
    public Task HandleAsync(FeatureFlagDeletedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing FeatureFlagDeletedIntegrationEvent - Key: {Key}", @event.Key);

        return moduleBus.PublishAsync(new FeatureFlagDeletedIntegrationEvent(
            @event.FeatureFlagId.Value,
            @event.Key,
            @event.TenantId,
            @event.OccurredAtUtc), ct);
    }
}

public sealed class FeatureFlagToggledDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<FeatureFlagToggledDomainEventHandler> logger) : IDomainEventHandler<FeatureFlagToggledDomainEvent>
{
    public Task HandleAsync(FeatureFlagToggledDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing FeatureFlagToggledIntegrationEvent - Key: {Key}, IsEnabled: {IsEnabled}", @event.Key, @event.IsEnabled);

        return moduleBus.PublishAsync(new FeatureFlagToggledIntegrationEvent(
            @event.FeatureFlagId.Value,
            @event.Key,
            @event.IsEnabled,
            @event.TenantId,
            @event.OccurredAtUtc), ct);
    }
}
