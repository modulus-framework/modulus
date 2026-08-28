using Modulus.Events.Abstractions;
using Modulus.Core.Abstractions;
using ProcureFlow.Modules.Configuration.Application.IntegrationEvents;
using ProcureFlow.Modules.Configuration.Domain.Events;
using Microsoft.Extensions.Logging;

using ProcureFlow.Modules.Configuration.Domain.ValueObjects;

namespace ProcureFlow.Modules.Configuration.Application.DomainEventHandlers;

public sealed class SettingUpdatedDomainEventHandler(
    IModuleBus moduleBus,
    ILogger<SettingUpdatedDomainEventHandler> logger) : IDomainEventHandler<SettingUpdatedDomainEvent>
{
    public Task HandleAsync(SettingUpdatedDomainEvent @event, CancellationToken ct)
    {
        logger.LogInformation("Publishing SettingUpdatedIntegrationEvent - SettingId: {SettingId}, Key: {Key}", @event.SettingId.Value, @event.Key);

        var integrationEvent = new SettingUpdatedIntegrationEvent(
            @event.SettingId.Value,
            @event.Key,
            @event.OldValue,
            @event.NewValue,
            Guid.Empty,
            @event.OccurredAtUtc);

        return moduleBus.PublishAsync(integrationEvent, ct);
    }
}
