using Microsoft.Extensions.Logging;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Rebus.Bus;
using Rebus.Handlers;

namespace Modulus.Sagas.Bus;

/// <summary>
/// <see cref="IModuleBus"/> implementation backed by a Rebus
/// <see cref="IBus"/>.  Publishing through this bus routes the integration
/// event through the configured Rebus transport (RabbitMQ, Azure Service Bus,
/// in-memory, …) so that saga handlers and regular handlers across all
/// service instances receive it.
/// </summary>
/// <remarks>
/// The ambient tenant id and correlation id are stamped onto outgoing
/// message headers (see <see cref="AmbientContextHeaders"/>) and restored by
/// <see cref="Modulus.Sagas.Pipeline.AmbientContextIncomingStep"/> on the
/// consumer side, so handlers run in the publisher's business context.
/// </remarks>
internal sealed class RebusModuleBus(
    IBus bus,
    ICurrentTenant currentTenant,
    ICorrelationContext? correlationContext,
    ILogger<RebusModuleBus>? logger = null) : IModuleBus
{
    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        logger?.LogDebug("Publishing {EventType} ({EventId}) via Rebus bus",
            typeof(TEvent).Name, @event.EventId);

        var headers = new Dictionary<string, string>();
        AmbientContextHeaders.Stamp(
            headers, currentTenant.TenantId, correlationContext?.CorrelationId);

        await bus.Publish(@event, headers);
    }
}

/// <summary>
/// <see cref="IOutboxDispatcher"/> implementation backed by a Rebus
/// <see cref="IBus"/>.  When the <c>OutboxProcessor</c> claims and dispatches
/// a message, this dispatcher deserialises the payload and publishes it
/// through Rebus instead of the in-process bus. The row's persisted tenant
/// and correlation ids ride along as message headers.
/// </summary>
internal sealed class RebusOutboxDispatcher(
    IBus bus,
    IIntegrationEventRegistry registry,
    IMessageSerializer serializer,
    ILogger<RebusOutboxDispatcher>? logger = null) : IOutboxDispatcher
{
    public async Task DispatchAsync(
        OutboxMessage message,
        CancellationToken ct)
    {
        // Resolve from the stable transport name via the registry; fall back to
        // Type.GetType for rows written by an older (AQN) version.
        if (!registry.TryGetType(message.MessageType, out var type))
            type = Type.GetType(message.MessageType);

        if (type is null)
            throw new InvalidOperationException(
                $"Cannot resolve outbox message type: {message.MessageType}");

        // Deserialise with the shared IMessageSerializer — the same options the
        // row was written with (camelCase + string enums). Raw JsonSerializer
        // defaults are case-sensitive and would silently drop init-only
        // properties (EventId/OccurredAt re-minted) and fail on enum payloads.
        var @event = (IIntegrationEvent?)(serializer.Deserialize(message.Payload, type))
            ?? throw new InvalidOperationException(
                $"Failed to deserialise outbox payload for {message.MessageType}");

        logger?.LogDebug("Dispatching outbox {Id} ({Type}) via Rebus bus",
            message.Id, type.Name);

        var headers = new Dictionary<string, string>();
        AmbientContextHeaders.Stamp(
            headers,
            message.TenantId == Guid.Empty ? null : message.TenantId,
            message.CorrelationId);

        await bus.Publish((dynamic)@event, headers);
    }
}

/// <summary>
/// Adapter that bridges Modulus <see cref="IIntegrationEventHandler{TEvent}"/>
/// registrations to Rebus's <see cref="IHandleMessages{TMessage}"/>
/// interface.  This lets existing Modulus integration-event handlers
/// (including those decorated with the inbox idempotency decorator) receive
/// messages dispatched through Rebus.
/// </summary>
internal sealed class IntegrationEventHandlerAdapter<TEvent>(
    IServiceProvider sp,
    ILogger<IntegrationEventHandlerAdapter<TEvent>>? logger = null)
    : IHandleMessages<TEvent>
    where TEvent : class, IIntegrationEvent
{
    public async Task Handle(TEvent message)
    {
        var handlers = sp.GetServices<IIntegrationEventHandler<TEvent>>()
            .ToList();

        if (handlers.Count == 0)
        {
            logger?.LogDebug(
                "No IIntegrationEventHandler<{Type}> registered; adapter is no-op.",
                typeof(TEvent).Name);
            return;
        }

        foreach (var handler in handlers)
            await handler.HandleAsync(message, CancellationToken.None);
    }
}
