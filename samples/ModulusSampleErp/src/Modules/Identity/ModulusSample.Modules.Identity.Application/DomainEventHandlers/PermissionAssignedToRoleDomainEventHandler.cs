using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.DomainEventHandlers;

internal sealed class PermissionAssignedToRoleDomainEventHandler(
    ICacheService cacheService,
    ILogger<PermissionAssignedToRoleDomainEventHandler> logger) : IDomainEventHandler<PermissionAssignedToRoleDomainEvent>
{
    public async Task HandleAsync(PermissionAssignedToRoleDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllPermissionsPrefix(), cancellationToken);
        await cacheService.RemoveByPrefixAsync(CacheKeys.User.AllRolesPrefix(), cancellationToken);

        logger.LogInformation(
            "Cache invalidated for permission assignment - RoleId: {RoleId}, PermissionId: {PermissionId}",
            domainEvent.RoleId.Value,
            domainEvent.PermissionId.Value);
    }
}
