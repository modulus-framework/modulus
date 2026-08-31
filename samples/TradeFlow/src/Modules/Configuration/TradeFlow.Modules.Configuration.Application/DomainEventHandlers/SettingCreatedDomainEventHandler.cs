using Modulus.Events.Abstractions;
using Modulus.Core.Abstractions;
using TradeFlow.Modules.Configuration.Application.IntegrationEvents;
using TradeFlow.Modules.Configuration.Domain.Events;
using Microsoft.Extensions.Logging;

using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Application.DomainEventHandlers;

public sealed class SettingCreatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<SettingCreatedDomainEventHandler> logger) : IDomainEventHandler<SettingCreatedDomainEvent>
{
    public Task HandleAsync(SettingCreatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing SettingCreatedIntegrationEvent - SettingId: {SettingId}, Key: {Key}", @event.SettingId.Value, @event.Key);

        var integrationEvent = new SettingCreatedIntegrationEvent(
            @event.SettingId.Value,
            @event.Key,
            @event.Category,
            false,
            @event.TenantId,
            @event.OccurredAtUtc);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}
