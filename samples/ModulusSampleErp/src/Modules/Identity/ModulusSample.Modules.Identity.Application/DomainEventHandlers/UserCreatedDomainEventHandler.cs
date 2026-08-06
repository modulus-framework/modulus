using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace ModulusSample.Modules.Identity.Application.DomainEventHandlers;

internal sealed class UserCreatedDomainEventHandler(
    ICacheService cacheService,
    ILogger<UserCreatedDomainEventHandler> logger) : IDomainEventHandler<UserCreatedDomainEvent>
{
    public async Task HandleAsync(UserCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidating cache for UserCreated - UserId: {UserId}", domainEvent.UserId);

        await cacheService.RemoveByPrefixAsync("user:", cancellationToken);

        logger.LogInformation("Cache invalidated for UserCreated - UserId: {UserId}", domainEvent.UserId);
    }
}
