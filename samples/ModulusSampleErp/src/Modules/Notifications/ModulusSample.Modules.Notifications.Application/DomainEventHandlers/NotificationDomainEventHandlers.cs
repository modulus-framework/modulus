using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using ModulusSample.Modules.Notifications.Application.IntegrationEvents;
using ModulusSample.Modules.Notifications.Domain.Events;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Modules.Notifications.Application.DomainEventHandlers;

public sealed class NotificationCreatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<NotificationCreatedDomainEventHandler> logger) : IDomainEventHandler<NotificationCreatedDomainEvent>
{
    public Task HandleAsync(NotificationCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing NotificationCreatedIntegrationEvent - NotificationId: {NotificationId}, Recipient: {Recipient}",
            @event.NotificationId.Value,
            @event.RecipientUserId);

        var integrationEvent = new NotificationCreatedIntegrationEvent(
            @event.NotificationId.Value,
            @event.RecipientUserId,
            @event.Title,
            @event.Message,
            @event.Type,
            @event.TenantId);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}

internal sealed class NotificationReadDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<NotificationReadDomainEventHandler> logger) : IDomainEventHandler<NotificationReadDomainEvent>
{
    public Task HandleAsync(NotificationReadDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation(
            "Publishing NotificationReadIntegrationEvent - NotificationId: {NotificationId}",
            @event.NotificationId.Value);

        var integrationEvent = new NotificationReadIntegrationEvent(
            @event.NotificationId.Value,
            @event.RecipientUserId,
            @event.TenantId);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}
