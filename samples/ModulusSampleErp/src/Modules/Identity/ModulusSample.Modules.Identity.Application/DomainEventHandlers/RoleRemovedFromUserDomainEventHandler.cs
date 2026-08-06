using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.DomainEventHandlers;

internal sealed class RoleRemovedFromUserDomainEventHandler(
    IUserRepository userRepository,
    ICacheService cacheService,
    ILogger<RoleRemovedFromUserDomainEventHandler> logger) : IDomainEventHandler<RoleRemovedFromUserDomainEvent>
{
    public async Task HandleAsync(RoleRemovedFromUserDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling RoleRemovedFromUserDomainEvent - UserId: {UserId}, RoleId: {RoleId}",
            domainEvent.UserId.Value, domainEvent.RoleId.Value);

        User? user = await userRepository.GetByIdAsync(domainEvent.UserId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("User not found - UserId: {UserId}", domainEvent.UserId.Value);
            return;
        }

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);
        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllPermissionsPrefix(), cancellationToken);

        logger.LogInformation("User roles and permissions cache invalidated - UserId: {UserId}", domainEvent.UserId.Value);
    }
}
