using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Modules.Identity.Application.IntegrationEvents;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.DomainEventHandlers;

internal sealed class UserDeletedDomainEventHandler(
    IUserRepository userRepository,
    IModuleBus moduleBus,
    ILogger<UserDeletedDomainEventHandler> logger) : IDomainEventHandler<UserDeletedDomainEvent>
{
    public async Task HandleAsync(
        UserDeletedDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling UserDeletedDomainEvent - UserId: {UserId}", domainEvent.UserId.Value);

        User? user = await userRepository.GetByIdAsync(domainEvent.UserId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("User not found during deletion event - UserId: {UserId}", domainEvent.UserId.Value);
            return;
        }

        await moduleBus.PublishAsync(
            new UserDeletedIntegrationEvent(
                domainEvent.UserId.Value,
                user.Email.Value),
            cancellationToken);

        logger.LogInformation("Published UserDeletedIntegrationEvent - UserId: {UserId}", domainEvent.UserId.Value);
    }
}
