using Modulus.Events.Abstractions;
using ModulusSample.Modules.Tenants.Application.IntegrationEvents;
using ModulusSample.Modules.Tenants.Domain.Events;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Modules.Tenants.Application.DomainEventHandlers;

public sealed class TenantCreatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantCreatedDomainEventHandler> logger) : IDomainEventHandler<TenantCreatedDomainEvent>
{
    public Task HandleAsync(TenantCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant created: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantCreatedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.Subdomain,
            @event.OccurredAt), ct);
    }
}

public sealed class TenantUpdatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantUpdatedDomainEventHandler> logger) : IDomainEventHandler<TenantUpdatedDomainEvent>
{
    public Task HandleAsync(TenantUpdatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant updated: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantUpdatedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.ModifiedBy,
            @event.OccurredAt), ct);
    }
}

public sealed class TenantActivatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantActivatedDomainEventHandler> logger) : IDomainEventHandler<TenantActivatedDomainEvent>
{
    public Task HandleAsync(TenantActivatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant activated: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantActivatedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.OccurredAt), ct);
    }
}

public sealed class TenantDeactivatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantDeactivatedDomainEventHandler> logger) : IDomainEventHandler<TenantDeactivatedDomainEvent>
{
    public Task HandleAsync(TenantDeactivatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant deactivated: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantDeactivatedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.OccurredAt), ct);
    }
}

public sealed class TenantDeletedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantDeletedDomainEventHandler> logger) : IDomainEventHandler<TenantDeletedDomainEvent>
{
    public Task HandleAsync(TenantDeletedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant deleted: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantDeletedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.DeletedBy,
            @event.OccurredAt), ct);
    }
}

public sealed class TenantFeaturesUpdatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantFeaturesUpdatedDomainEventHandler> logger) : IDomainEventHandler<TenantFeaturesUpdatedDomainEvent>
{
    public Task HandleAsync(TenantFeaturesUpdatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant features updated: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantFeaturesUpdatedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.ModifiedBy,
            @event.OccurredAt), ct);
    }
}

public sealed class TenantSettingsUpdatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<TenantSettingsUpdatedDomainEventHandler> logger) : IDomainEventHandler<TenantSettingsUpdatedDomainEvent>
{
    public Task HandleAsync(TenantSettingsUpdatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing integration event for tenant settings updated: {TenantId}", @event.TenantId);

        return moduleBus.PublishAsync(new TenantSettingsUpdatedIntegrationEvent(
            @event.TenantId,
            @event.Name,
            @event.ModifiedBy,
            @event.OccurredAt), ct);
    }
}
