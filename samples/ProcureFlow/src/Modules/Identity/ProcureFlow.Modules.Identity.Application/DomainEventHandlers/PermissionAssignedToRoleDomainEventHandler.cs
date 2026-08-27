using ProcureFlow.Modules.Identity.Domain.Events;
using ProcureFlow.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Identity.Application.DomainEventHandlers;

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
