namespace Modulus.Realtime;

using Modulus.Events.Abstractions;

/// <summary>An integration event pushed to clients (registered with <see cref="RealtimeBuilder.AddEvent{TEvent}"/>).</summary>
internal sealed record RealtimeEventDescriptor(Type EventType, string Name, Func<object, RealtimeAudience> Audience, Func<object, object?>? Payload);

/// <summary>
/// Pushes an integration event to its audience. An ordinary integration event handler, so it runs wherever the event is
/// handled (the module bus, the outbox relay, a broker consumer) in that event's tenant; the backplane takes it to the
/// node each client is connected to.
/// </summary>
internal sealed class RealtimeEventHandler<TEvent>(IEnumerable<RealtimeEventDescriptor> descriptors, IRealtimePublisher publisher)
    : IIntegrationEventHandler<TEvent>
    where TEvent : class, IIntegrationEvent
{
    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(@event);
        foreach (var descriptor in descriptors)
        {
            if (descriptor.EventType != typeof(TEvent))
                continue;
            var data = descriptor.Payload is null ? @event : descriptor.Payload(@event);
            await publisher.PublishAsync(descriptor.Name, data, descriptor.Audience(@event), ct).ConfigureAwait(false);
        }
    }
}
