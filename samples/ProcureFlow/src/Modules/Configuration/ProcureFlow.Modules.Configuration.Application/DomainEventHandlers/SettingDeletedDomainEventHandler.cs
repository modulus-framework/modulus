using Modulus.Events.Abstractions;
using Modulus.Core.Abstractions;
using ProcureFlow.Modules.Configuration.Application.IntegrationEvents;
using ProcureFlow.Modules.Configuration.Domain.Events;
using Microsoft.Extensions.Logging;

using ProcureFlow.Modules.Configuration.Domain.ValueObjects;

namespace ProcureFlow.Modules.Configuration.Application.DomainEventHandlers;

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
