using ProcureFlow.Modules.Identity.Domain.Events;
using ProcureFlow.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Identity.Application.DomainEventHandlers;

internal sealed class RoleCreatedDomainEventHandler(
    ICacheService cacheService,
    ILogger<RoleCreatedDomainEventHandler> logger) : IDomainEventHandler<RoleCreatedDomainEvent>
{
    public async Task HandleAsync(RoleCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling RoleCreatedDomainEvent - RoleId: {RoleId}, RoleName: {RoleName}",
            domainEvent.RoleId.Value, domainEvent.RoleName);

        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);
        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllPermissionsPrefix(), cancellationToken);

        logger.LogInformation("Roles and permissions cache invalidated - RoleId: {RoleId}", domainEvent.RoleId.Value);
    }
}
