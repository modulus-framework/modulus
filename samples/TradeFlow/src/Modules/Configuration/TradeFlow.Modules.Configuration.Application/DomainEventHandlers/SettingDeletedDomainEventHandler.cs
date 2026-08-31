using Modulus.Events.Abstractions;
using Modulus.Core.Abstractions;
using TradeFlow.Modules.Configuration.Application.IntegrationEvents;
using TradeFlow.Modules.Configuration.Domain.Events;
using Microsoft.Extensions.Logging;

using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Application.DomainEventHandlers;

public sealed class SettingDeletedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<SettingDeletedDomainEventHandler> logger) : IDomainEventHandler<SettingDeletedDomainEvent>
{
    public Task HandleAsync(SettingDeletedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing SettingDeletedIntegrationEvent - SettingId: {SettingId}, Key: {Key}", @event.SettingId.Value, @event.Key);

        var integrationEvent = new SettingDeletedIntegrationEvent(
            @event.SettingId.Value,
            @event.Key,
            Guid.Empty,
            @event.OccurredAtUtc);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}
