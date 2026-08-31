using TradeFlow.Modules.Identity.Domain.Events;
using TradeFlow.Shared.Application.Caching;
using Microsoft.Extensions.Logging;
using Modulus.Events.Abstractions;

namespace TradeFlow.Modules.Identity.Application.DomainEventHandlers;

internal sealed class SessionRevokedDomainEventHandler(
    ICacheService cacheService,
    ILogger<SessionRevokedDomainEventHandler> logger) : IDomainEventHandler<SessionRevokedDomainEvent>
{
    public async Task HandleAsync(SessionRevokedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing SessionRevoked domain event - SessionId: {SessionId}, UserId: {UserId}, Reason: {Reason}",
            domainEvent.SessionId, domainEvent.UserId, domainEvent.Reason);

        await cacheService.RemoveByPrefixAsync($"session:{domainEvent.UserId}:", cancellationToken);
        await cacheService.RemoveByPrefixAsync($"sessions:user:{domainEvent.UserId}", cancellationToken);

        logger.LogInformation(
            "Cache invalidated for revoked session - SessionId: {SessionId}, UserId: {UserId}",
            domainEvent.SessionId, domainEvent.UserId);
    }
}
