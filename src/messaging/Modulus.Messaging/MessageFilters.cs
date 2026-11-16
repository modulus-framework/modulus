namespace Modulus.Messaging;

/// <summary>Where a filter runs: a message being sent, published, or consumed.</summary>
public enum MessageFilterPoint
{
    /// <summary>A message sent to one endpoint.</summary>
    Send = 0,

    /// <summary>A message published to every subscribed endpoint, once per envelope.</summary>
    Publish = 1,

    /// <summary>One consume attempt, inside its DI scope.</summary>
    Consume = 2,
}

/// <summary>What a filter sees: where it runs, the envelope, and the services of the scope it runs in.</summary>
public sealed class MessageFilterContext
{
    internal MessageFilterContext(MessageFilterPoint point, MessageEnvelope envelope, IServiceProvider services)
    {
        Point = point;
        Envelope = envelope;
        Services = services;
    }

    public MessageFilterPoint Point { get; }

    public MessageEnvelope Envelope { get; }

    public IServiceProvider Services { get; }
}

/// <summary>
/// A step around every message at the points it is registered for (FR-20). A filter calls <c>next</c> to continue; it
/// may throw to refuse the message (a send or publish then fails for the caller; a consume is retried or parked like a
/// consumer failure). Filters are resolved from the DI container when a message runs, so a singleton or scoped
/// registration is honoured; a filter that is not registered is created by the bus.
/// </summary>
public interface IMessageFilter
{
    Task Execute(MessageFilterContext context, Func<Task> next);
}

/// <summary>
/// Sees the bus's activity without changing it (FR-20). Each method is optional. Observers are created once, when the bus
/// is built; they run after the event they name, and an exception in one is logged and does not affect the message.
/// </summary>
public interface IMessageObserver
{
    /// <summary>The bus started (its transport is receiving).</summary>
    Task OnBusStarted() => Task.CompletedTask;

    /// <summary>The bus is stopping; in-flight messages are draining.</summary>
    Task OnBusStopping() => Task.CompletedTask;

    /// <summary>A message was sent to an endpoint.</summary>
    Task OnSent(MessageEnvelope envelope) => Task.CompletedTask;

    /// <summary>A message was published (one call per envelope, one per subscribed endpoint).</summary>
    Task OnPublished(MessageEnvelope envelope) => Task.CompletedTask;

    /// <summary>A delivery was received from the transport, before it is checked or consumed.</summary>
    Task OnReceived(MessageEnvelope envelope) => Task.CompletedTask;

    /// <summary>A consumer handled the message.</summary>
    Task OnConsumed(MessageEnvelope envelope) => Task.CompletedTask;

    /// <summary>A consumer attempt failed (before any retry, redelivery or park decision).</summary>
    Task OnConsumeFaulted(MessageEnvelope envelope, Exception exception) => Task.CompletedTask;
}
