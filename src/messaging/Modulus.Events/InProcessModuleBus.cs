namespace Modulus.Events;

using System.Diagnostics;
using Modulus.Events.Abstractions;

/// <summary>
/// Default IModuleBus implementation.
/// Dispatches integration events in-process to all registered handlers.
/// Swap for RabbitMQ/ASB/Kafka adapter via Outbox dispatcher config.
/// </summary>
internal sealed class InProcessModuleBus(IServiceProvider sp)
    : IModuleBus
{
    private static readonly ActivitySource s_activitySource = new("Modulus.Events");

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var eventName = typeof(TEvent).Name;
        using var activity = s_activitySource.StartActivity(
            "message publish",
            ActivityKind.Producer);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "modulus");
            activity.SetTag("messaging.operation", "publish");
            activity.SetTag("messaging.destination.name", eventName);
            activity.SetTag("messaging.message.id", @event.EventId.ToString("N"));
        }

        var handlers = sp
            .GetServices<IIntegrationEventHandler<TEvent>>();

        // Wrap each resolved handler (e.g. with inbox dedup) at dispatch time —
        // see IIntegrationEventHandlerDecorator's remarks. Optional: falls back
        // to invoking the raw handler when no such feature is registered.
        var decorator = sp.GetService<IIntegrationEventHandlerDecorator>();

        foreach (var handler in handlers)
        {
            var target = decorator is null
                ? handler
                : (IIntegrationEventHandler<TEvent>)decorator.Decorate(sp, typeof(TEvent), handler);
            await target.HandleAsync(@event, ct);
        }
    }
}
