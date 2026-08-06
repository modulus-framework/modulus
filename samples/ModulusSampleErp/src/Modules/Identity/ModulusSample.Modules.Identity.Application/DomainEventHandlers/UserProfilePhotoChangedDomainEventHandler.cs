using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Modules.Identity.Application.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.DomainEventHandlers;

internal sealed class UserProfilePhotoChangedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<UserProfilePhotoChangedDomainEventHandler> logger) : IDomainEventHandler<UserProfilePhotoChangedEvent>
{
    public async Task HandleAsync(
        UserProfilePhotoChangedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Publishing integration event for UserProfilePhotoChangedEvent - UserId: {UserId}",
            domainEvent.UserId);

        await moduleBus.PublishAsync(
            new UserProfilePhotoChangedIntegrationEvent(
                domainEvent.UserId,
                domainEvent.OldProfilePhotoStoragePath),
            cancellationToken);
    }
}
