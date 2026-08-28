using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Events;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ProcureFlow.Modules.Identity.Application.DomainEventHandlers;

internal sealed class RoleAssignedToUserDomainEventHandler(
    IUserRepository userRepository,
    ICacheService cacheService,
    ILogger<RoleAssignedToUserDomainEventHandler> logger) : IDomainEventHandler<RoleAssignedToUserDomainEvent>
{
    public async Task HandleAsync(RoleAssignedToUserDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        User? user = await userRepository.GetByIdAsync(domainEvent.UserId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning(
                "Role assignment @event received for non-existent user: {UserId}, RoleId: {RoleId}",
                domainEvent.UserId.Value,
                domainEvent.RoleId.Value);
            return;
        }

        logger.LogInformation(
            "Role assigned to user: {UserId}, Email: {Email}, RoleId: {RoleId}, AssignedAt: {AssignedAt}",
            domainEvent.UserId.Value,
            user.Email.Value,
            domainEvent.RoleId.Value,
            domainEvent.AssignedAtUtc);

        await cacheService.RemoveByPrefixAsync("user:roles:", cancellationToken);
        await cacheService.RemoveByPrefixAsync("user:permissions:", cancellationToken);

        logger.LogInformation("Cache invalidated for role assignment - UserId: {UserId}, RoleId: {RoleId}",
            domainEvent.UserId.Value, domainEvent.RoleId.Value);
    }
}
